using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Starlights.Integration.Drivers.CharacterCreation;
using Starlights.Integration.Extensions;
using Starlights.Modules.Characters.Data.EntityFramework;
using Starlights.Modules.Characters.Domain.Characters;
using Starlights.Modules.Elements.Data.EntityFramework;
using Starlights.Modules.Elements.Domain.Homebrew;
using Starlights.Modules.Elements.Endpoints.Installation;
using Starlights.Modules.Elements.Integration;

namespace Starlights.Integration.Tests.Elements;

/// <summary>
/// The Homebrew page's edit preview (POST /api/elements/homebrew/{kind}/preview) and the revision a save can send:
/// the preview is admin only and writes nothing; it says what changes, who uses the record and what they would lose,
/// and refuses what the save refuses; a save based on a preview is refused (409, with a fresh preview) when the record
/// changed or went away since, and a save without one works as it always did. Made-up homebrew in a temporary folder.
/// </summary>
/// <remarks>
/// On SQL Server (<c>STARFALL_TEST_SQLSERVER</c>, as <see cref="HomebrewSqlServerTests"/>): a Homebrew page save
/// imports its whole file again, and the in-memory provider keeps a removed element's components, so a second save
/// into a file cannot be shown there.
/// </remarks>
[TestClass]
[TestCategory("SqlServer")]
public sealed class HomebrewEditPreviewTests : IntegrationTestBase
{
    private const string OldMessage = "A homebrew spell needs a name (100 characters at most), a level from 0 to 9, a school, and class lists made of letters.";

    private IntegrationHost _integration = default!;
    private string _connection = default!;
    private string _root = default!;
    private string _homebrew = default!;

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
        _connection = new SqlConnectionStringBuilder(server) { InitialCatalog = $"starfall_prev_{Guid.NewGuid():N}"[..28] }.ConnectionString;
        await using (var elements = new ElementsContext(new DbContextOptionsBuilder<ElementsContext>().UseSqlServer(_connection).Options))
        {
            await elements.Database.MigrateAsync();
        }
        await using (var characters = new CharactersContext(new DbContextOptionsBuilder<CharactersContext>().UseSqlServer(_connection).Options))
        {
            await characters.Database.MigrateAsync();
        }

        _root = Directory.CreateTempSubdirectory("starfall-homebrew-preview").FullName;
        _homebrew = Path.Combine(_root, "homebrew");
        var content = Directory.CreateDirectory(Path.Combine(_root, "content")).FullName;
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
            .Build();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _integration?.Dispose();
        if (_connection is not null)
        {
            // as HomebrewSqlServerTests: not EnsureDeleted, which clears every connection pool
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

    private static HomebrewSpellModel Spell(string name = "Test Sparkle", int level = 1) => new()
    {
        Name = name,
        Source = "Test Homebrew",
        Level = level,
        School = "Evocation",
        Lists = ["Wizard"],
        Description = "A made-up spell. It deals 1d6 radiant damage.",
    };

    private string SpellsFile => Path.Combine(_homebrew, "starlights-spells.xml");

    private async Task<HomebrewEditPreview> PreviewAsync(string kind, object model)
    {
        using var admin = _integration.CreateAdminClient();
        var response = await admin.PostAsJsonAsync($"/api/elements/homebrew/{kind}/preview", model, _integration.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(_integration.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<HomebrewEditPreview>(_integration.CancellationToken))!;
    }

    private async Task<HttpResponseMessage> SaveAsync(string kind, object model)
    {
        using var admin = _integration.CreateAdminClient();
        return await admin.PutAsJsonAsync($"/api/elements/homebrew/{kind}", model, _integration.CancellationToken);
    }

    private async Task<T> SavedAsync<T>(string kind, T model)
    {
        var response = await SaveAsync(kind, model!);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(_integration.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<T>(_integration.CancellationToken))!;
    }

    private async Task<List<HomebrewSpellModel>> SpellsAsync()
    {
        using var client = _integration.CreateClient();
        return (await client.GetFromJsonAsync<HomebrewSpellsResponse>("/api/elements/homebrew/spells", _integration.CancellationToken))!.Spells;
    }

    [TestMethod]
    public async Task Preview_NeedsTheAdminKey_AndWritesNothing()
    {
        await _integration.InitializeElements();
        using var anonymous = _integration.CreateClient();

        var refused = await anonymous.PostAsJsonAsync("/api/elements/homebrew/spells/preview", Spell(), _integration.CancellationToken);
        var preview = await PreviewAsync("spells", Spell());

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "it names the characters that use homebrew");
        preview.Should().BeEquivalentTo(new
        {
            Kind = "spells",
            Id = "ID_STARLIGHTS_HOMEBREW_SPELL_TEST_SPARKLE",
            IsNew = true,
            Revision = (string?)null,
            Changes = Array.Empty<HomebrewFieldChange>(),
            Characters = Array.Empty<HomebrewAffectedCharacter>(),
            Diagnostics = Array.Empty<ImportDiagnostic>(),
        });
        preview.Elements.Should().Equal(new HomebrewElementChange("ID_STARLIGHTS_HOMEBREW_SPELL_TEST_SPARKLE", "Test Sparkle", HomebrewElementChangeKind.Added));
        File.Exists(SpellsFile).Should().BeFalse("a preview saves nothing");
        (await SpellsAsync()).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ASavedSpell_HasARevision_AndPreviewingItUnchangedChangesNothing()
    {
        await _integration.InitializeElements();
        var saved = await SavedAsync("spells", Spell());

        var listed = (await SpellsAsync()).Single();
        var preview = await PreviewAsync("spells", listed);

        saved.Revision.Should().NotBeNullOrEmpty();
        listed.Revision.Should().Be(saved.Revision, "what a save answers is what a read gives");
        preview.IsNew.Should().BeFalse();
        preview.Revision.Should().Be(saved.Revision);
        preview.Changes.Should().BeEmpty();
        preview.Elements.Should().BeEmpty();
        preview.Diagnostics.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AnEditsPreview_ListsWhatChanges_AndWhoUsesIt_AndWritesNothing()
    {
        await _integration.InitializeElements();
        var saved = await SavedAsync("spells", Spell());
        var characterId = await _integration.GetDriver<CharacterCreationDriver>().CreateCharacterAsync();
        using (var admin = _integration.CreateAdminClient())
        {
            var spell = AuroraIds.ToGuid(saved.Id!);
            var magic = await admin.PutAsJsonAsync($"/api/characters/{characterId}/magic", new CharacterMagic { Prepared = new() { ["Wizard"] = [spell] } }, _integration.CancellationToken);
            magic.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        var file = await File.ReadAllBytesAsync(SpellsFile);

        var preview = await PreviewAsync("spells", saved with { Level = 2, Lists = ["Wizard", "Sorcerer"] });

        preview.Changes.Should().Equal(new HomebrewFieldChange("level", "1", "2"), new HomebrewFieldChange("lists", "Wizard", "Wizard, Sorcerer"));
        preview.Elements.Should().Equal(new HomebrewElementChange(saved.Id!, "Test Sparkle", HomebrewElementChangeKind.Changed));
        preview.Characters.Should().ContainSingle().Which.Should().BeEquivalentTo(new { CharacterId = characterId, Uses = new[] { saved.Id }, Loses = Array.Empty<string>() });
        (await File.ReadAllBytesAsync(SpellsFile)).Should().Equal(file, "a preview saves nothing");
        (await SpellsAsync()).Single().Level.Should().Be(1);
    }

    [TestMethod]
    public async Task ASaveBasedOnAnOlderRevision_IsRefused_WithAFreshPreview()
    {
        await _integration.InitializeElements();
        var first = await SavedAsync("spells", Spell());
        // saved again elsewhere, without a revision: as before, it simply saves
        var second = await SavedAsync("spells", first with { Level = 3, Revision = null });
        var file = await File.ReadAllBytesAsync(SpellsFile);

        var stale = await SaveAsync("spells", first with { Level = 2, ExpectedRevision = first.Revision });
        var preview = (await stale.Content.ReadFromJsonAsync<HomebrewEditPreview>(_integration.CancellationToken))!;

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        preview.Revision.Should().Be(second.Revision).And.NotBe(first.Revision);
        preview.Diagnostics.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Severity = ImportDiagnosticSeverity.Error, Code = ImportDiagnosticCode.StaleRevision, ElementId = first.Id });
        preview.Changes.Should().Equal(new HomebrewFieldChange("level", "3", "2"));
        (await File.ReadAllBytesAsync(SpellsFile)).Should().Equal(file, "nothing was saved");

        (await SavedAsync("spells", first with { Level = 2, ExpectedRevision = second.Revision })).Level.Should().Be(2, "with the current revision it saves");
    }

    [TestMethod]
    public async Task ASaveBasedOnARemovedRecord_IsRefused()
    {
        await _integration.InitializeElements();
        var saved = await SavedAsync("spells", Spell());
        using (var admin = _integration.CreateAdminClient())
        {
            (await admin.DeleteAsync($"/api/elements/homebrew/spells/{saved.Id}", _integration.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var stale = await SaveAsync("spells", saved with { ExpectedRevision = saved.Revision });
        var preview = (await stale.Content.ReadFromJsonAsync<HomebrewEditPreview>(_integration.CancellationToken))!;

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        preview.IsNew.Should().BeTrue();
        preview.Diagnostics.Single().Message.Should().Be("It was removed after you opened it, so it was not saved.");
        (await SpellsAsync()).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ANewRecordNamedLikeASavedOne_IsFlagged()
    {
        await _integration.InitializeElements();
        var saved = await SavedAsync("spells", Spell());

        var preview = await PreviewAsync("spells", Spell(level: 4));

        preview.IsNew.Should().BeFalse();
        preview.Diagnostics.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Severity = ImportDiagnosticSeverity.Warning, Code = ImportDiagnosticCode.ReplacesExisting, ElementId = saved.Id });
    }

    [TestMethod]
    public async Task ThePreviewAndTheSave_RefuseTheSameSpells()
    {
        await _integration.InitializeElements();
        HomebrewSpellModel[] spells =
        [
            Spell(),
            Spell(name: " "),
            Spell(name: new string('x', 101)),
            Spell(level: 10),
            Spell() with { School = "Chronurgy" },
            Spell() with { Range = new string('x', 101) },
            Spell() with { MaterialText = new string('x', 301) },
            Spell() with { HigherLevels = new string('x', 5_001) },
            Spell() with { Lists = ["Wizard!"] },
            Spell() with { Lists = [.. Enumerable.Range(0, 21).Select(i => $"List {i}")] },
            Spell(name: "Test Second", level: 0) with { Lists = [], MaterialText = new string('x', 300) },
        ];

        foreach (var spell in spells)
        {
            var preview = await PreviewAsync("spells", spell);
            var save = await SaveAsync("spells", spell);
            var refused = preview.Diagnostics.Any(d => d.Severity == ImportDiagnosticSeverity.Error);

            (save.StatusCode == HttpStatusCode.BadRequest).Should().Be(refused, "the preview and the save agree on {0}", spell.Name);
            preview.Diagnostics.Where(d => d.Severity == ImportDiagnosticSeverity.Error).Should().OnlyContain(d => d.Code == ImportDiagnosticCode.InvalidValue && d.Detail != null);
            if (refused)
            {
                (await save.Content.ReadAsStringAsync(_integration.CancellationToken)).Should().Contain(OldMessage, "the save's message is as it was");
                preview.Elements.Should().BeEmpty("nothing to compare when it cannot be saved");
            }
        }
    }

    [TestMethod]
    public async Task ASubclassEdit_ThatDropsAFeature_SaysWhoLosesIt()
    {
        await _integration.InitializeElements();
        var cls = await SavedAsync("classes", new HomebrewClassModel { Name = "Test Warden", Source = "Test Homebrew", HitDie = 10, SavingThrows = ["Strength", "Wisdom"] });
        var subclass = await SavedAsync("subclasses", new HomebrewSubclassModel
        {
            Name = "Test Circle",
            Source = "Test Homebrew",
            ClassSupports = cls.SubclassSupports!,
            ClassName = cls.Name,
            Features = [new() { Level = 3, Name = "Test Ward" }, new() { Level = 6, Name = "Test Echo" }],
        });
        var echo = subclass.Features.Single(f => f.Name == "Test Echo").Id!;
        var characterId = await _integration.GetDriver<CharacterCreationDriver>().CreateCharacterAsync();
        using (var admin = _integration.CreateAdminClient())
        {
            // the preview asks who uses the feature's element however they came by it; a prepared entry stands in for
            // a builder pick here (picks are found by their registrations, which the SQL Server suite covers)
            var magic = new CharacterMagic { Prepared = new() { ["Test"] = [AuroraIds.ToGuid(echo)] } };
            (await admin.PutAsJsonAsync($"/api/characters/{characterId}/magic", magic, _integration.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var preview = await PreviewAsync("subclasses", subclass with { Features = [.. subclass.Features.Where(f => f.Id != echo)] });

        preview.Elements.Should().Equal(
            new HomebrewElementChange(subclass.Id!, "Test Circle", HomebrewElementChangeKind.Changed),
            new HomebrewElementChange(echo, "Level 6: Test Echo", HomebrewElementChangeKind.Removed));
        preview.Characters.Should().ContainSingle().Which.Should().BeEquivalentTo(new { CharacterId = characterId, Uses = new[] { echo }, Loses = new[] { echo } });
        preview.Changes.Should().BeEmpty("the features are listed as elements, not as a field");
    }
}
