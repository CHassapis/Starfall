using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Starlights.Integration.Drivers.CharacterCreation;
using Starlights.Integration.Extensions;
using Starlights.Modules.Characters.Data;
using Starlights.Modules.Characters.Data.EntityFramework;
using Starlights.Modules.Elements.Data.EntityFramework;
using Starlights.Platform.Data;

namespace Starlights.Integration.Tests.Characters;

/// <summary>
/// <see cref="ICharacterSnapshotsRepository.DeleteRowsAsync"/>, what undoes a half-made character import, against a
/// real SQL Server database (the in-memory provider runs no SQL): it used to fail on its own statement
/// (character_snapshot has no column "id"), which left the half-made character behind.
/// </summary>
/// <remarks>Needs <c>STARFALL_TEST_SQLSERVER</c>, like <see cref="Elements.HomebrewSqlServerTests"/>.</remarks>
[TestClass]
[TestCategory("SqlServer")]
public sealed class CharacterRowsSqlServerTests : IntegrationTestBase
{
    private IntegrationHost _integration = default!;
    private string _connection = default!;
    private string _root = default!;

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
            await elements.Database.MigrateAsync(TestContext.CancellationToken);
        }
        await using (var characters = new CharactersContext(new DbContextOptionsBuilder<CharactersContext>().UseSqlServer(_connection).Options))
        {
            await characters.Database.MigrateAsync(TestContext.CancellationToken);
        }

        _root = Directory.CreateTempSubdirectory("starfall-sqlserver-tests").FullName;
        _integration = IntegrationHost.CreateDefaultBuilder(this)
            .ConfigureOptions(o =>
            {
                o.TestTimeout = TimeSpan.FromMinutes(2);
                o.Settings["Integration:SqlServer"] = _connection;
                o.Settings["Aurora:ContentPath"] = Directory.CreateDirectory(Path.Combine(_root, "content")).FullName;
                o.Settings["Aurora:HomebrewPath"] = Directory.CreateDirectory(Path.Combine(_root, "homebrew")).FullName;
                o.Settings["Aurora:PullRequestsPath"] = Path.Combine(_root, "no-pull-requests");
                o.Settings["FiveETools:Path"] = Path.Combine(_root, "no-5etools");
            })
            .Build();
        await _integration.InitializeElements();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _integration?.Dispose();
        if (_connection is not null)
        {
            // as in HomebrewSqlServerTests: not EnsureDeleted, which clears every connection pool
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
    public async Task DeleteRows_RemovesTheCharacterAndEveryRowItSpans_AndNoOtherCharacter()
    {
        var creation = _integration.GetDriver<CharacterCreationDriver>();
        var kept = await creation.CreateCharacterAsync();
        var removed = await creation.CreateCharacterAsync();
        await WaitForProcessingAsync(kept, removed);

        var keptBefore = await CaptureAsync(kept);
        // a builder snapshot as well, so the snapshot table's row goes too
        await ExecuteAsync("INSERT INTO characters.character_snapshot (character_id, data, taken_at) VALUES (@id, @data, SYSUTCDATETIME())",
            removed, await CaptureAsync(removed));
        (await RowsAsync(removed)).Values.Should().Contain(n => n > 0, "the character has rows before it is removed");

        using (var scope = _integration.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPersistence>().GetRepository<ICharacterSnapshotsRepository>().DeleteRowsAsync(removed);
        }

        (await RowsAsync(removed)).Should().OnlyContain(table => table.Value == 0, "every row of the removed character is gone");
        (await CaptureAsync(kept)).Should().Be(keptBefore, "the other character is untouched");
    }

    private async Task WaitForProcessingAsync(params Guid[] characters)
    {
        foreach (var character in characters)
        {
            for (var tries = 0; tries < 200; tries++)
            {
                using var scope = _integration.Services.CreateScope();
                if (!await scope.ServiceProvider.GetRequiredService<IPersistence>().GetRepository<ICharacterSnapshotsRepository>().IsProcessingAsync(character))
                {
                    break;
                }
                await Task.Delay(100, TestContext.CancellationToken);
            }
        }
    }

    private async Task<string> CaptureAsync(Guid character)
    {
        using var scope = _integration.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPersistence>().GetRepository<ICharacterSnapshotsRepository>().CaptureAsync(character);
    }

    /// <summary>The character's rows in each table it spans (including those reached through its components and registrations).</summary>
    private async Task<Dictionary<string, int>> RowsAsync(Guid character)
    {
        string[] queries =
        [
            "character WHERE id = @id",
            "character_snapshot WHERE character_id = @id",
            "registration WHERE character_id = @id",
            "registration_include_rules WHERE parent_registration_id IN (SELECT id FROM characters.registration WHERE character_id = @id)",
            "component_abilities WHERE parent_character = @id",
            "component_appearance WHERE parent_character = @id",
            "component_class WHERE parent_character = @id",
            "component_progression WHERE parent_character = @id",
            "component_saving_throws WHERE parent_character = @id",
            "component_skills WHERE parent_character = @id",
        ];
        var rows = new Dictionary<string, int>();
        await using var connection = new SqlConnection(_connection);
        await connection.OpenAsync(TestContext.CancellationToken);
        foreach (var query in queries)
        {
            await using var command = new SqlCommand("SELECT COUNT(*) FROM characters." + query, connection);
            command.Parameters.AddWithValue("@id", character);
            rows[query.Split(' ')[0]] = (int)(await command.ExecuteScalarAsync(TestContext.CancellationToken))!;
        }
        return rows;
    }

    private async Task ExecuteAsync(string sql, Guid character, string data)
    {
        await using var connection = new SqlConnection(_connection);
        await connection.OpenAsync(TestContext.CancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", character);
        command.Parameters.AddWithValue("@data", data);
        await command.ExecuteNonQueryAsync(TestContext.CancellationToken);
    }
}
