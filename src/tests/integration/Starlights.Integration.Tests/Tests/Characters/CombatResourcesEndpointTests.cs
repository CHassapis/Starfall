using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Starlights.Integration.Drivers.CharacterCreation;
using Starlights.Integration.Extensions;
using Starlights.Modules.Characters.Data.EntityFramework;
using Starlights.Modules.Characters.Domain.Characters;
using Starlights.Modules.Characters.Endpoints.Characters.CreateCharacter;
using Starlights.Modules.Characters.Endpoints.Characters.Players;
using Starlights.Modules.Elements.Data.EntityFramework;

namespace Starlights.Integration.Tests.Characters;

/// <summary>
/// The player's own resources and favorite rolls travel with the character's combat state (GET and PUT
/// /api/characters/{id}/combat): what was saved comes back, a save from an app that does not know them keeps them, an
/// empty list removes them, the state's own checks refuse what is out of bounds, and a password-locked player's
/// character needs that player's token for them as for everything else. Synthetic characters and players only.
/// </summary>
/// <remarks>
/// On SQL Server (<c>STARFALL_TEST_SQLSERVER</c>, as <see cref="Elements.HomebrewSqlServerTests"/>): saving the combat
/// state takes a row lock, which the in-memory provider cannot.
/// </remarks>
[TestClass]
[TestCategory("SqlServer")]
public sealed class CombatResourcesEndpointTests : IntegrationTestBase
{
    private const string PlayerTokenHeader = "X-Player-Token";

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
        _connection = new SqlConnectionStringBuilder(server) { InitialCatalog = $"starfall_res_{Guid.NewGuid():N}"[..28] }.ConnectionString;
        await using (var elements = new ElementsContext(new DbContextOptionsBuilder<ElementsContext>().UseSqlServer(_connection).Options))
        {
            await elements.Database.MigrateAsync();
        }
        await using (var characters = new CharactersContext(new DbContextOptionsBuilder<CharactersContext>().UseSqlServer(_connection).Options))
        {
            await characters.Database.MigrateAsync();
        }

        _root = Directory.CreateTempSubdirectory("starfall-resources").FullName;
        var content = Directory.CreateDirectory(Path.Combine(_root, "content")).FullName;
        _integration = IntegrationHost.CreateDefaultBuilder(this)
            .ConfigureOptions(o =>
            {
                o.TestTimeout = TimeSpan.FromMinutes(2);
                o.Settings["Integration:SqlServer"] = _connection;
                o.Settings["Aurora:ContentPath"] = content;
                o.Settings["Aurora:HomebrewPath"] = Path.Combine(_root, "homebrew");
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

    private static readonly List<CharacterResource> Resources =
    [
        new() { Id = "r1", Name = "Test Pool", Max = new() { From = "class-level", ClassName = "Paladin", Times = 5 }, Recharge = "dawn" },
        new() { Id = "r2", Name = "Test Touch", Pool = "Test Pool", Cost = 5, Effect = new() { ArmorClass = 1, Damage = "1d6 fire" } },
    ];

    [TestMethod]
    [Timeout(TestConstants.Timeout, CooperativeCancellation = true)]
    public async Task Resources_AreSavedWithWhatWasSpent_AndASaveWithoutThemKeepsThem()
    {
        var characterId = await _integration.GetDriver<CharacterCreationDriver>().CreateCharacterAsync();
        using var client = _integration.CreateClient();
        var url = $"/api/characters/{characterId}/combat";

        var saved = await client.PutAsJsonAsync(url, new CharacterCombat { Uses = new() { ["custom:r1"] = 3 }, Effects = ["custom:r2"], Resources = Resources }, _integration.CancellationToken);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(_integration.CancellationToken));
        var read = (await client.GetFromJsonAsync<CharacterCombat>(url, _integration.CancellationToken))!;
        read.Resources.Should().BeEquivalentTo(Resources);
        read.Uses.Should().Equal(new Dictionary<string, int> { ["custom:r1"] = 3 });
        read.Effects.Should().Equal("custom:r2");

        // an app from before resources sends none: they stay, what it changed is saved
        var older = await client.PutAsJsonAsync(url, new { version = 1, damage = 4, uses = new Dictionary<string, int> { ["custom:r1"] = 2 } }, _integration.CancellationToken);
        older.StatusCode.Should().Be(HttpStatusCode.OK);
        read = (await client.GetFromJsonAsync<CharacterCombat>(url, _integration.CancellationToken))!;
        read.Damage.Should().Be(4);
        read.Uses.Should().Equal(new Dictionary<string, int> { ["custom:r1"] = 2 });
        read.Resources.Should().BeEquivalentTo(Resources, "a save that leaves them out keeps them");

        // an empty list removes them
        (await client.PutAsJsonAsync(url, new CharacterCombat { Resources = [] }, _integration.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<CharacterCombat>(url, _integration.CancellationToken))!.Resources.Should().BeEmpty();
    }

    [TestMethod]
    [Timeout(TestConstants.Timeout, CooperativeCancellation = true)]
    public async Task ResourcesOutOfBounds_AreRefused_AndChangeNothing()
    {
        var characterId = await _integration.GetDriver<CharacterCreationDriver>().CreateCharacterAsync();
        using var client = _integration.CreateClient();
        var url = $"/api/characters/{characterId}/combat";
        (await client.PutAsJsonAsync(url, new CharacterCombat { Resources = Resources }, _integration.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var refused = await client.PutAsJsonAsync(url, new CharacterCombat { Resources = [Resources[0] with { Recharge = "sometimes" }] }, _integration.CancellationToken);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(_integration.CancellationToken)).Should().Contain("known recharge");
        (await client.GetFromJsonAsync<CharacterCombat>(url, _integration.CancellationToken))!.Resources.Should().BeEquivalentTo(Resources);
    }

    [TestMethod]
    [Timeout(TestConstants.Timeout, CooperativeCancellation = true)]
    public async Task LockedPlayersCharacter_ItsResources_NeedThatPlayersToken()
    {
        var (playerName, ownToken) = await LockPlayerAsync("Test Player Ilse", "synthetic-pass-9");
        var (_, otherToken) = await LockPlayerAsync("Test Player Jory", "synthetic-pass-10");
        var characterId = await CreateCharacterForAsync(playerName, ownToken);
        var state = new CharacterCombat { Resources = [Resources[0]] };

        (await PutCombatAsync(characterId, state)).Should().Be(HttpStatusCode.Unauthorized, "a locked player's character needs a token");
        (await PutCombatAsync(characterId, state, otherToken)).Should().Be(HttpStatusCode.Unauthorized, "another player's token does not change it");
        (await GetCombatAsync(characterId, otherToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "nor read it");
        (await GetCombatAsync(characterId, ownToken)).Resources.Should().BeNullOrEmpty("nothing was saved without the token");
        (await PutCombatAsync(characterId, state, ownToken)).Should().Be(HttpStatusCode.OK);
        (await GetCombatAsync(characterId, ownToken)).Resources.Should().BeEquivalentTo([Resources[0]]);
    }

    private static readonly List<CharacterFavoriteRoll> FavoriteRolls =
    [
        new() { Id = "f1", Name = "Test Smite", Formula = "2d8+3" },
        new() { Id = "f2", Name = "Test Luck", Formula = "d20" },
    ];

    [TestMethod]
    [Timeout(TestConstants.Timeout, CooperativeCancellation = true)]
    public async Task FavoriteRolls_AreSaved_AndASaveWithoutThemKeepsThem()
    {
        var characterId = await _integration.GetDriver<CharacterCreationDriver>().CreateCharacterAsync();
        using var client = _integration.CreateClient();
        var url = $"/api/characters/{characterId}/combat";

        var saved = await client.PutAsJsonAsync(url, new CharacterCombat { Damage = 2, Resources = Resources, FavoriteRolls = FavoriteRolls }, _integration.CancellationToken);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(_integration.CancellationToken));
        var read = (await client.GetFromJsonAsync<CharacterCombat>(url, _integration.CancellationToken))!;
        read.FavoriteRolls.Should().BeEquivalentTo(FavoriteRolls, o => o.WithStrictOrdering());

        // an app from before favorite rolls sends none: they stay, as do the resources
        (await client.PutAsJsonAsync(url, new { version = 1, damage = 5 }, _integration.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        read = (await client.GetFromJsonAsync<CharacterCombat>(url, _integration.CancellationToken))!;
        read.Damage.Should().Be(5);
        read.FavoriteRolls.Should().BeEquivalentTo(FavoriteRolls, o => o.WithStrictOrdering(), "a save that leaves them out keeps them");
        read.Resources.Should().BeEquivalentTo(Resources);

        // an edited roll and a removed one; an empty list removes them all
        (await client.PutAsJsonAsync(url, new CharacterCombat { FavoriteRolls = [FavoriteRolls[0] with { Formula = "3d8+3" }] }, _integration.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<CharacterCombat>(url, _integration.CancellationToken))!.FavoriteRolls.Should().BeEquivalentTo([FavoriteRolls[0] with { Formula = "3d8+3" }]);
        (await client.PutAsJsonAsync(url, new CharacterCombat { FavoriteRolls = [] }, _integration.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<CharacterCombat>(url, _integration.CancellationToken))!.FavoriteRolls.Should().BeEmpty();
    }

    [TestMethod]
    [Timeout(TestConstants.Timeout, CooperativeCancellation = true)]
    public async Task FavoriteRollsThatAreNotDice_AndEmptyEntries_AreRefused_AndChangeNothing()
    {
        var characterId = await _integration.GetDriver<CharacterCreationDriver>().CreateCharacterAsync();
        using var client = _integration.CreateClient();
        var url = $"/api/characters/{characterId}/combat";
        (await client.PutAsJsonAsync(url, new CharacterCombat { FavoriteRolls = FavoriteRolls }, _integration.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var formula in new[] { "2d6 fire", "101d6", "1d6*2" })
        {
            var refused = await client.PutAsJsonAsync(url, new CharacterCombat { FavoriteRolls = [FavoriteRolls[0] with { Formula = formula }] }, _integration.CancellationToken);
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, formula);
            (await refused.Content.ReadAsStringAsync(_integration.CancellationToken)).Should().Contain("favorite roll");
        }

        // a list with an empty entry, or a roll or resource without its fields: refused with a message, not a server error
        var empties = new object[]
        {
            new { favoriteRolls = new object?[] { null } },
            new { resources = new object?[] { null } },
            new { favoriteRolls = new[] { new { id = (string?)null, name = (string?)null, formula = (string?)null } } },
            new { resources = new[] { new { id = "r9", name = "Test Empty", max = (object?)null } } },
        };
        foreach (var body in empties)
        {
            var refused = await client.PutAsJsonAsync(url, body, _integration.CancellationToken);
            refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync(_integration.CancellationToken));
        }
        var read = (await client.GetFromJsonAsync<CharacterCombat>(url, _integration.CancellationToken))!;
        read.FavoriteRolls.Should().BeEquivalentTo(FavoriteRolls, o => o.WithStrictOrdering());
        read.Resources.Should().BeNullOrEmpty();
    }

    [TestMethod]
    [Timeout(TestConstants.Timeout, CooperativeCancellation = true)]
    public async Task LockedPlayersCharacter_ItsFavoriteRolls_NeedThatPlayersToken()
    {
        var (playerName, ownToken) = await LockPlayerAsync("Test Player Maren", "synthetic-pass-11");
        var (_, otherToken) = await LockPlayerAsync("Test Player Oswin", "synthetic-pass-12");
        var characterId = await CreateCharacterForAsync(playerName, ownToken);
        var state = new CharacterCombat { FavoriteRolls = FavoriteRolls };

        (await PutCombatAsync(characterId, state)).Should().Be(HttpStatusCode.Unauthorized, "a locked player's character needs a token");
        (await PutCombatAsync(characterId, state, otherToken)).Should().Be(HttpStatusCode.Unauthorized, "another player's token does not change it");
        (await PutCombatAsync(characterId, state, ownToken)).Should().Be(HttpStatusCode.OK);
        using var client = _integration.CreateClient();
        client.DefaultRequestHeaders.Add(PlayerTokenHeader, otherToken);
        (await client.GetAsync($"/api/characters/{characterId}/combat", _integration.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "nor read them");
        using var own = _integration.CreateClient();
        own.DefaultRequestHeaders.Add(PlayerTokenHeader, ownToken);
        (await own.GetFromJsonAsync<CharacterCombat>($"/api/characters/{characterId}/combat", _integration.CancellationToken))!.FavoriteRolls.Should().BeEquivalentTo(FavoriteRolls);
    }

    /// <summary>Locks a new synthetic player with a password (allowed while it has none) and returns its token.</summary>
    private async Task<(string Name, string Token)> LockPlayerAsync(string name, string password)
    {
        using var client = _integration.CreateClient();
        var response = await client.PutAsJsonAsync("/api/characters/players/password", new SetPlayerPasswordRequest { Name = name, Password = password }, _integration.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<UnlockPlayerResponse>(_integration.CancellationToken))!;
        return (body.Name, body.Token);
    }

    private async Task<Guid> CreateCharacterForAsync(string playerName, string playerToken)
    {
        var option = await _integration.GetDriver<CharacterCreationOptionsDriver>().GetDefaultCharacterCreationOptionAsync();
        using var client = _integration.CreateClient();
        client.DefaultRequestHeaders.Add(PlayerTokenHeader, playerToken);
        var response = await client.PostAsJsonAsync("/api/characters", new CreateCharacterRequest(option.Id, "Locked Character", PlayerName: playerName), _integration.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreateCharacterResponse>(_integration.CancellationToken))!.Id;
    }

    private async Task<HttpStatusCode> PutCombatAsync(Guid characterId, CharacterCombat state, string? playerToken = null)
    {
        using var client = _integration.CreateClient();
        if (playerToken is not null)
        {
            client.DefaultRequestHeaders.Add(PlayerTokenHeader, playerToken);
        }
        return (await client.PutAsJsonAsync($"/api/characters/{characterId}/combat", state, _integration.CancellationToken)).StatusCode;
    }

    private async Task<(HttpStatusCode StatusCode, List<CharacterResource>? Resources)> GetCombatAsync(Guid characterId, string playerToken)
    {
        using var client = _integration.CreateClient();
        client.DefaultRequestHeaders.Add(PlayerTokenHeader, playerToken);
        var response = await client.GetAsync($"/api/characters/{characterId}/combat", _integration.CancellationToken);
        return (response.StatusCode, response.IsSuccessStatusCode ? (await response.Content.ReadFromJsonAsync<CharacterCombat>(_integration.CancellationToken))!.Resources : null);
    }
}
