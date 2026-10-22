using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Transactions;
using System.Xml.Linq;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Starlights.Integration.Drivers.CharacterCreation;
using Starlights.Integration.Extensions;
using Starlights.Modules.Characters.Data.EntityFramework;
using Starlights.Modules.Elements.Data;
using Starlights.Modules.Elements.Data.EntityFramework;
using Starlights.Modules.Elements.Domain;
using Starlights.Modules.Elements.Domain.Components;
using Starlights.Modules.Elements.Integration;
using Starlights.Platform.Data;

namespace Starlights.Integration.Tests.Elements;

/// <summary>
/// Homebrew uploads, replacements and deletions, and Aurora replace imports, against a real SQL Server database
/// (the in-memory provider ignores transactions, so rollback can only be shown here). Failures are injected into
/// the actual saves and into the transaction's commit; afterwards every row of the elements and characters schemas
/// and the homebrew file must be as they were.
/// </summary>
/// <remarks>
/// Needs <c>STARFALL_TEST_SQLSERVER</c>: a connection string to a disposable SQL Server (no database; each test
/// creates and drops its own). Without it the tests are inconclusive, unless <c>STARFALL_REQUIRE_SQLSERVER</c> is
/// set (CI), where they fail.
/// </remarks>
[TestClass]
[TestCategory("SqlServer")]
public sealed class HomebrewSqlServerTests : IntegrationTestBase
{
    private const string Base = """
        <elements>
          <element name="Arcana" type="Proficiency" source="Mk2 Test" id="ID_MK2_SKILL"><supports>Skill,Intelligence</supports></element>
          <element name="Wizardry" type="Class" source="Mk2 Test" id="ID_MK2_CLASS">
            <supports>Mk2 Class</supports>
            <rules><select type="Proficiency" name="Skill" supports="Skill" number="2" /></rules>
          </element>
          <element name="Elf" type="Race" source="Mk2 Test" id="ID_MK2_RACE"><supports>Mk2 Race</supports></element>
          <element name="Sage" type="Background" source="Mk2 Test" id="ID_MK2_BACKGROUND"><supports>Mk2 Background</supports></element>
          <element name="Alert" type="Feat" source="Mk2 Test" id="ID_MK2_FEAT"><supports>Mk2 Feat</supports></element>
          <element name="Light" type="Spell" source="Mk2 Test" id="ID_MK2_SPELL"><supports>Mk2 Spell</supports></element>
        </elements>
        """;

    private const string Index = """
        <index><files><file name="mk2-base.xml" url="https://example.invalid/master/mk2-base.xml" /></files></index>
        """;

    private static string Append(string support, string id = "ID_MK2_SKILL") =>
        $"""<elements><append id="{id}"><supports>{support}</supports></append></elements>""";

    private IntegrationHost _integration = default!;
    private string _connection = default!;
    private string _root = default!;
    private string _homebrew = default!;
    private readonly InjectedFailures _failures = new();

    [TestInitialize]
    public async Task Initialize()
    {
        var server = Environment.GetEnvironmentVariable("STARFALL_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(server))
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STARFALL_REQUIRE_SQLSERVER")))
            {
                Assert.Fail("STARFALL_REQUIRE_SQLSERVER is set but STARFALL_TEST_SQLSERVER is not");
            }
            Assert.Inconclusive("STARFALL_TEST_SQLSERVER is not set to a disposable SQL Server");
        }

        _connection = new SqlConnectionStringBuilder(server) { InitialCatalog = $"starfall_test_{Guid.NewGuid():N}"[..28] }.ConnectionString;
        await using (var elements = new ElementsContext(new DbContextOptionsBuilder<ElementsContext>().UseSqlServer(_connection).Options))
        {
            await elements.Database.MigrateAsync();
        }
        await using (var characters = new CharactersContext(new DbContextOptionsBuilder<CharactersContext>().UseSqlServer(_connection).Options))
        {
            await characters.Database.MigrateAsync();
        }

        _root = Directory.CreateTempSubdirectory("starfall-sqlserver-tests").FullName;
        var content = Directory.CreateDirectory(Path.Combine(_root, "content")).FullName;
        _homebrew = Directory.CreateDirectory(Path.Combine(_root, "homebrew")).FullName;
        await File.WriteAllTextAsync(Path.Combine(content, "mk2-base.xml"), Base);
        await File.WriteAllTextAsync(Path.Combine(content, "mk2.index"), Index);

        _integration = IntegrationHost.CreateDefaultBuilder(this)
            .ConfigureOptions(o =>
            {
                o.TestTimeout = TimeSpan.FromMinutes(2);
                o.Settings["Integration:SqlServer"] = _connection;
                o.Settings["Aurora:ContentPath"] = content;
                o.Settings["Aurora:HomebrewPath"] = _homebrew;
                o.Settings["Aurora:PullRequestsPath"] = Path.Combine(_root, "no-pull-requests");
                o.Settings["FiveETools:Path"] = Path.Combine(_root, "no-5etools");
            })
            .ConfigureServices(services => services.ConfigureDbContext<ElementsContext>(o => o.AddInterceptors(_failures)))
            .Build();

        // a character that has nothing to do with the homebrew: none of its rows may change
        await _integration.InitializeElements();
        await _integration.GetDriver<CharacterCreationDriver>().CreateCharacterAsync();

        (await Import("mk2.index")).Should().Be(HttpStatusCode.OK);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _integration?.Dispose();
        if (_connection is not null)
        {
            // not EnsureDeleted: it clears every connection pool, and a test whose transaction loses its pooled
            // connection that way has to open a second one, which SQL Server can only do as a distributed transaction
            var database = new SqlConnectionStringBuilder(_connection);
            SqlConnection.ClearPool(new SqlConnection(_connection));
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(_connection) { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            await using var drop = new SqlCommand($"ALTER DATABASE [{database.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database.InitialCatalog}];", master);
            await drop.ExecuteNonQueryAsync();
        }
        if (_root is not null)
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AppendLifecycle_PatchesTargets_AndRestoresThem_WithStableIds()
    {
        var ids = await ElementIds();
        var original = await StoredXml();
        var unrelated = await Snapshot("characters");

        // append-only file, supported target types, a missing target
        (await Upload("a.xml", Append("Patched A"))).Should().Be(HttpStatusCode.OK);
        (await Supports("ID_MK2_SKILL")).Should().BeEquivalentTo(["Skill", "Intelligence", "Patched A"]);
        foreach (var id in new[] { "ID_MK2_CLASS", "ID_MK2_RACE", "ID_MK2_BACKGROUND", "ID_MK2_FEAT", "ID_MK2_SPELL" })
        {
            (await Upload($"types-{id}.xml", Append("Patched " + id, id))).Should().Be(HttpStatusCode.OK);
            (await Supports(id)).Should().Contain("Patched " + id);
        }

        // a second file patching the same element, and a mixed file (its own element plus an append)
        (await Upload("b.xml", Append("Patched B"))).Should().Be(HttpStatusCode.OK);
        (await Upload("mixed.xml", """
            <elements>
              <element name="Homebrew Feat" type="Feat" source="Mk2 Test" id="ID_MK2_HOMEBREW_FEAT"><supports>Mk2 Feat</supports></element>
              <append id="ID_MK2_SKILL"><supports>Patched Mixed</supports></append>
            </elements>
            """)).Should().Be(HttpStatusCode.OK);
        (await Supports("ID_MK2_SKILL")).Should().BeEquivalentTo(["Skill", "Intelligence", "Patched A", "Patched B", "Patched Mixed"]);

        // repeating an upload and an import adds nothing twice
        var before = await Snapshot("elements");
        (await Upload("b.xml", Append("Patched B"))).Should().Be(HttpStatusCode.OK);
        (await Import("homebrew", update: true)).Should().Be(HttpStatusCode.OK);
        (await Snapshot("elements")).Should().Equal(before);

        // invalid files change nothing
        (await Upload("bad.xml", "<elements><append id=\"ID_MK2_SKILL\">")).Should().Be(HttpStatusCode.BadRequest);
        (await Upload("blank.xml", "<elements><append id=\" \"><supports>X</supports></append></elements>")).Should().Be(HttpStatusCode.BadRequest);
        (await Snapshot("elements")).Should().Equal(before);
        File.Exists(Path.Combine(_homebrew, "bad.xml")).Should().BeFalse();

        // replacing the first file swaps its patch, deleting files removes theirs and keeps the others
        (await Upload("a.xml", Append("Patched A2"))).Should().Be(HttpStatusCode.OK);
        (await Supports("ID_MK2_SKILL")).Should().BeEquivalentTo(["Skill", "Intelligence", "Patched A2", "Patched B", "Patched Mixed"]);
        (await Delete("a.xml")).Should().Be(HttpStatusCode.OK);
        (await Supports("ID_MK2_SKILL")).Should().BeEquivalentTo(["Skill", "Intelligence", "Patched B", "Patched Mixed"]);
        (await Delete("mixed.xml")).Should().Be(HttpStatusCode.OK);
        (await ElementIds()).Should().NotContainKey("ID_MK2_HOMEBREW_FEAT");
        (await Delete("b.xml")).Should().Be(HttpStatusCode.OK);
        foreach (var id in ids.Keys.Where(k => k != "ID_MK2_SKILL"))
        {
            (await Delete($"types-{id}.xml")).Should().Be(HttpStatusCode.OK);
        }

        // everything as it was: the same element ids, the same stored XML, the unrelated character untouched
        (await ElementIds()).Should().Equal(ids);
        (await StoredXml()).Should().Equal(original);
        (await Snapshot("characters")).Should().Equal(unrelated);
        (await Supports("ID_MK2_SKILL")).Should().BeEquivalentTo(["Skill", "Intelligence"]);
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    [DataRow(Failure.FirstSave, DisplayName = "fails before anything is saved")]
    [DataRow(Failure.RebuildSave, DisplayName = "fails after the deletions were saved")]
    [DataRow(Failure.Commit, DisplayName = "fails while committing")]
    public async Task ReplacingAHomebrewFile_ThatFails_LeavesDatabaseAndFileAsTheyWere(Failure failure)
    {
        (await Upload("a.xml", Append("Patched A"))).Should().Be(HttpStatusCode.OK);
        var file = await File.ReadAllBytesAsync(Path.Combine(_homebrew, "a.xml"));
        var elements = await Snapshot("elements");
        var characters = await Snapshot("characters");

        _failures.Arm(failure);
        (await Upload("a.xml", Append("Patched A2"))).Should().Be(HttpStatusCode.InternalServerError);
        _failures.Triggered.Should().BeTrue();

        (await Snapshot("elements")).Should().Equal(elements);
        (await Snapshot("characters")).Should().Equal(characters);
        (await File.ReadAllBytesAsync(Path.Combine(_homebrew, "a.xml"))).Should().Equal(file);
        (await Supports("ID_MK2_SKILL")).Should().Contain("Patched A").And.NotContain("Patched A2");
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    [DataRow(Failure.FirstSave)]
    [DataRow(Failure.Commit)]
    public async Task UploadingANewHomebrewFile_ThatFails_RemovesTheFileAgain(Failure failure)
    {
        var elements = await Snapshot("elements");

        _failures.Arm(failure);
        (await Upload("new.xml", Append("Patched New"))).Should().Be(HttpStatusCode.InternalServerError);

        (await Snapshot("elements")).Should().Equal(elements);
        File.Exists(Path.Combine(_homebrew, "new.xml")).Should().BeFalse();
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    [DataRow(Failure.FirstSave, DisplayName = "fails removing the file's elements")]
    [DataRow(Failure.RebuildSave, DisplayName = "fails rebuilding the patched targets")]
    [DataRow(Failure.Commit, DisplayName = "fails while committing")]
    public async Task DeletingAHomebrewFile_ThatFails_KeepsItsElementsAndFile(Failure failure)
    {
        (await Upload("mixed.xml", """
            <elements>
              <element name="Homebrew Feat" type="Feat" source="Mk2 Test" id="ID_MK2_HOMEBREW_FEAT"><supports>Mk2 Feat</supports></element>
              <append id="ID_MK2_SKILL"><supports>Patched Mixed</supports></append>
            </elements>
            """)).Should().Be(HttpStatusCode.OK);
        var file = await File.ReadAllBytesAsync(Path.Combine(_homebrew, "mixed.xml"));
        var elements = await Snapshot("elements");

        _failures.Arm(failure);
        (await Delete("mixed.xml")).Should().Be(HttpStatusCode.InternalServerError);
        _failures.Triggered.Should().BeTrue();

        (await Snapshot("elements")).Should().Equal(elements);
        (await File.ReadAllBytesAsync(Path.Combine(_homebrew, "mixed.xml"))).Should().Equal(file);
        (await ElementIds()).Should().ContainKey("ID_MK2_HOMEBREW_FEAT");
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    [DataRow(Failure.RebuildSave, DisplayName = "fails after the deletions were saved")]
    [DataRow(Failure.Commit, DisplayName = "fails while committing")]
    public async Task ReplaceImport_ThatFails_DoesNotLoseTheExistingElements(Failure failure)
    {
        var ids = await ElementIds();
        var elements = await Snapshot("elements");

        _failures.Arm(failure);
        (await Import("mk2.index", replace: true)).Should().Be(HttpStatusCode.InternalServerError);
        _failures.Triggered.Should().BeTrue();

        (await Snapshot("elements")).Should().Equal(elements);
        (await ElementIds()).Should().Equal(ids);

        // and without the failure the same replace import goes through, keeping the ids
        (await Import("mk2.index", replace: true)).Should().Be(HttpStatusCode.OK);
        (await ElementIds()).Should().Equal(ids);
    }

    [TestMethod]
    public async Task WhoUsesAnElement_IsFoundByTheirRegistrations_AndAPreviewChangesNoRow()
    {
        var characterId = await _integration.GetDriver<CharacterCreationDriver>().CreateCharacterAsync();
        using var admin = _integration.CreateAdminClient();
        var registrations = await admin.GetFromJsonAsync<JsonObject>($"/api/characters/{characterId}/registrations", _integration.CancellationToken);
        var picked = Guid.Parse(registrations!["registrations"]![0]!["associatedElementId"]!.GetValue<string>());

        IReadOnlyList<ElementUser> users;
        using (var scope = _integration.Services.CreateScope())
        {
            users = await scope.ServiceProvider.GetRequiredService<IElementUsage>().GetUsersAsync([picked, Guid.NewGuid()], _integration.CancellationToken);
        }

        users.Should().Contain(u => u.CharacterId == characterId, "the character registered it");
        users.Should().OnlyContain(u => u.ElementIds.SequenceEqual(new[] { picked }), "only the element asked about that they use");

        var characters = await Snapshot("characters");
        var elements = await Snapshot("elements");
        var preview = await admin.PostAsJsonAsync("/api/elements/homebrew/spells/preview",
            new { Name = "Mk2 Spark", Level = 1, School = "Evocation", Lists = new[] { "Wizard" }, Description = "Made up." }, _integration.CancellationToken);

        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Snapshot("characters")).Should().Equal(characters, "a preview reads only");
        (await Snapshot("elements")).Should().Equal(elements, "a preview reads only");
        File.Exists(Path.Combine(_homebrew, "starlights-spells.xml")).Should().BeFalse();
    }

    // ---- the API

    private async Task<HttpStatusCode> Send(Func<HttpClient, Task<HttpResponseMessage>> request)
    {
        using var client = _integration.CreateAdminClient();
        try
        {
            using var response = await request(client);
            return response.StatusCode;
        }
        catch (Exception ex) when (ex is not AssertFailedException && _failures.Triggered)
        {
            // the test server rethrows what the application did not handle; a real server answers 500
            return HttpStatusCode.InternalServerError;
        }
    }

    private Task<HttpStatusCode> Upload(string file, string content) =>
        Send(c => c.PostAsJsonAsync("/api/elements/homebrew", new { FileName = file, Content = content }));

    private Task<HttpStatusCode> Delete(string file) =>
        Send(c => c.DeleteAsync($"/api/elements/homebrew/{Uri.EscapeDataString(file)}"));

    private Task<HttpStatusCode> Import(string index, bool replace = false, bool update = false) =>
        Send(c => c.PostAsJsonAsync("/api/elements/import-aurora", new { Index = index, Replace = replace, Update = update }));

    // ---- what is stored

    private async Task<T> WithRepository<T>(Func<IElementsRepository, Task<T>> read)
    {
        using var scope = _integration.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<IPersistence>().GetRepository<IElementsRepository>());
    }

    private Task<Dictionary<string, Guid>> ElementIds() => WithRepository(async r =>
        (await r.GetElementIdsByAuroraIdsAsync(["ID_MK2_SKILL", "ID_MK2_CLASS", "ID_MK2_RACE", "ID_MK2_BACKGROUND", "ID_MK2_FEAT", "ID_MK2_SPELL", "ID_MK2_HOMEBREW_FEAT"]))
            .OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary());

    private Task<List<string>> StoredXml() => WithRepository(async r =>
        (await r.GetAuroraXmlAsync()).OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}").ToList());

    private Task<List<string>> Supports(string auroraId) => WithRepository(async r =>
    {
        var id = (await r.GetElementIdsByAuroraIdsAsync([auroraId]))[auroraId];
        var element = await r.GetElementAsync(id) ?? throw new AssertFailedException($"{auroraId} is gone");
        return element.Components.OfType<SupportsComponent>().SelectMany(s => s.Supports).ToList();
    });

    /// <summary>Row count and checksum of every table of the schema, except the domain-event outbox (processed in the background).</summary>
    private async Task<List<string>> Snapshot(string schema)
    {
        await using var connection = new SqlConnection(_connection);
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = new SqlCommand("SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID(@schema) AND name NOT LIKE '%Event%' ORDER BY name", connection))
        {
            list.Parameters.AddWithValue("@schema", schema);
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }
        var rows = new List<string>();
        foreach (var table in tables)
        {
            await using var count = new SqlCommand($"SELECT COUNT(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM [{schema}].[{table}]", connection);
            await using var reader = await count.ExecuteReaderAsync();
            await reader.ReadAsync();
            rows.Add($"{table}: {reader.GetInt32(0)} rows, checksum {(reader.IsDBNull(1) ? "-" : reader.GetInt32(1))}");
        }
        rows.Should().NotBeEmpty();
        return rows;
    }

    // ---- failure injection

    public enum Failure
    {
        /// <summary>The first save of the elements (for a replacement: the one with the deletions).</summary>
        FirstSave,

        /// <summary>The save that adds the rebuilt elements, after the deletions were saved in the same transaction.</summary>
        RebuildSave,

        /// <summary>The transaction itself: a participant votes to roll back while it commits.</summary>
        Commit,
    }

    private sealed class InjectedFailures : SaveChangesInterceptor
    {
        private Failure? _armed;
        private bool _enlisted;

        public bool Triggered { get; private set; }

        public void Arm(Failure failure)
        {
            _armed = failure;
            _enlisted = false;
            Triggered = false;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            // only the saves that change elements: the domain-event outbox saves through the same context type
            var elements = eventData.Context!.ChangeTracker.Entries<Element>().ToList();
            if (!elements.Any(e => e.State is EntityState.Added or EntityState.Deleted or EntityState.Modified))
            {
                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }
            switch (_armed)
            {
                case Failure.FirstSave:
                    Fire();
                    break;
                case Failure.RebuildSave when elements.Any(e => e.State == EntityState.Added):
                    Fire();
                    break;
                case Failure.Commit when !_enlisted:
                    var transaction = Transaction.Current ?? throw new AssertFailedException("the save runs outside a transaction");
                    transaction.EnlistVolatile(new RollbackWhilePreparing(this), EnlistmentOptions.None);
                    _enlisted = true;
                    break;
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Fire()
        {
            _armed = null;
            Triggered = true;
            throw new InvalidOperationException("injected save failure");
        }

        private sealed class RollbackWhilePreparing(InjectedFailures owner) : IEnlistmentNotification
        {
            public void Prepare(PreparingEnlistment preparingEnlistment)
            {
                owner._armed = null;
                owner.Triggered = true;
                preparingEnlistment.ForceRollback(new InvalidOperationException("injected commit failure"));
            }

            public void Commit(Enlistment enlistment) => enlistment.Done();

            public void Rollback(Enlistment enlistment) => enlistment.Done();

            public void InDoubt(Enlistment enlistment) => enlistment.Done();
        }
    }
}
