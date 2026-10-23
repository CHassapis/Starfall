using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Starlights.Integration.Drivers.CharacterCreation;
using Starlights.Integration.Extensions;
using Starlights.Modules.Characters.Endpoints.Characters.CreateCharacter;
using Starlights.Modules.Characters.Endpoints.Characters.Players;
using Starlights.Modules.Elements.Endpoints.Content.Attributes.AbilityScores.Create;
using Starlights.Modules.Elements.Endpoints.Content.Attributes.AbilityScores.GetList;

namespace Starlights.Integration.Tests.Security;

/// <summary>
/// The application's access checks over HTTP, with the host's synthetic credentials: content administration needs
/// the admin key or the admin token (players only read content), and a password-locked player's characters need that
/// player's token or the admin token. Nothing here uses real players, passwords or keys.
/// </summary>
[TestClass]
public sealed class AccessControlEndpointTests : IntegrationTestBase
{
    private const string PlayerTokenHeader = "X-Player-Token";
    private const string CreateAbilityScoreUrl = "/api/elements/ability-scores/create";

    private IntegrationHost _integration = default!;

    [TestInitialize]
    public void Initialize()
    {
        _integration = IntegrationHost.CreateDefaultBuilder(this)
            .Build();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _integration.Dispose();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // content administration

    [TestMethod]
    public async Task ContentWrite_WithoutCredentials_IsForbiddenAndChangesNothing()
    {
        using var client = _integration.CreateClient();

        var response = await client.PostAsJsonAsync(CreateAbilityScoreUrl, new CreateAbilityScoreRequest("Strength", "STR", null), _integration.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AbilityScoreNamesAsync()).Should().NotContain("Strength");
    }

    [TestMethod]
    public async Task ContentWrite_WithWrongAdminKey_IsForbidden()
    {
        using var client = _integration.CreateClient();
        client.DefaultRequestHeaders.Add(IntegrationHost.AdminKeyHeader, "not-the-admin-key");

        var response = await client.PostAsJsonAsync(CreateAbilityScoreUrl, new CreateAbilityScoreRequest("Strength", "STR", null), _integration.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task ContentWrite_WithAdminKey_IsAllowed()
    {
        using var client = _integration.CreateAdminClient();

        var response = await client.PostAsJsonAsync(CreateAbilityScoreUrl, new CreateAbilityScoreRequest("Strength", "STR", null), _integration.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await AbilityScoreNamesAsync()).Should().Contain("Strength");
    }

    [TestMethod]
    public async Task ContentWrite_WithAdminTokenFromMasterPassword_IsAllowed()
    {
        var adminToken = await UnlockAdminAsync(_integration.Options.MasterPassword);
        using var client = _integration.CreateClient();
        client.DefaultRequestHeaders.Add(PlayerTokenHeader, adminToken);

        var response = await client.PostAsJsonAsync(CreateAbilityScoreUrl, new CreateAbilityScoreRequest("Strength", "STR", null), _integration.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [TestMethod]
    public async Task ContentWrite_WithAPlayersToken_IsForbidden()
    {
        var (_, playerToken) = await LockPlayerAsync("Test Player Ash", "synthetic-pass-1");
        using var client = _integration.CreateClient();
        client.DefaultRequestHeaders.Add(PlayerTokenHeader, playerToken);

        var response = await client.PostAsJsonAsync(CreateAbilityScoreUrl, new CreateAbilityScoreRequest("Strength", "STR", null), _integration.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task ContentRead_WithoutCredentials_IsAllowed()
    {
        using var client = _integration.CreateClient();

        var response = await client.GetAsync("/api/elements/ability-scores", _integration.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task InitializingTheRuleset_WithoutCredentials_IsForbidden()
    {
        using var client = _integration.CreateClient();

        var response = await client.GetAsync("/api/elements/initialize", _integration.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task AdminEndpoints_WithoutCredentials_AreForbidden()
    {
        using var client = _integration.CreateClient();

        var response = await client.GetAsync("/api/admin/status", _integration.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task AdminUnlock_WithWrongMasterPassword_IsUnauthorized()
    {
        using var client = _integration.CreateClient();

        var response = await client.PostAsJsonAsync("/api/admin/unlock", new AdminUnlockRequest { Password = "not-the-master-password" }, _integration.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // password-locked players

    [TestMethod]
    public async Task LockedPlayersCharacter_OnlyOpensWithThatPlayersTokenOrTheAdminToken()
    {
        await _integration.InitializeElements();
        var (playerName, ownToken) = await LockPlayerAsync("Test Player Bryn", "synthetic-pass-2");
        var (_, otherToken) = await LockPlayerAsync("Test Player Cael", "synthetic-pass-3");
        var adminToken = await UnlockAdminAsync(_integration.Options.MasterPassword);
        var characterId = await CreateCharacterForAsync(playerName, ownToken);

        (await GetCharacterAsync(characterId)).Should().Be(HttpStatusCode.Unauthorized, "a locked player's character needs a token");
        (await GetCharacterAsync(characterId, otherToken)).Should().Be(HttpStatusCode.Unauthorized, "another player's token does not open it");
        (await GetCharacterAsync(characterId, adminKey: _integration.Options.AdminKey)).Should().Be(HttpStatusCode.Unauthorized, "the content admin key is not a player's token");
        (await GetCharacterAsync(characterId, ownToken)).Should().Be(HttpStatusCode.OK);
        (await GetCharacterAsync(characterId, adminToken)).Should().Be(HttpStatusCode.OK, "the admin token opens every player");
    }

    [TestMethod]
    public async Task LockedPlayersCharacter_ItsCalculationTrace_NeedsTheSameToken()
    {
        await _integration.InitializeElements();
        var (playerName, ownToken) = await LockPlayerAsync("Test Player Gale", "synthetic-pass-7");
        var (_, otherToken) = await LockPlayerAsync("Test Player Hollis", "synthetic-pass-8");
        var adminToken = await UnlockAdminAsync(_integration.Options.MasterPassword);
        var characterId = await CreateCharacterForAsync(playerName, ownToken);
        const string trace = "/calculation/trace?value=skill:athletics";

        (await GetCharacterAsync(characterId, path: trace)).Should().Be(HttpStatusCode.Unauthorized, "a locked player's character needs a token");
        (await GetCharacterAsync(characterId, otherToken, path: trace)).Should().Be(HttpStatusCode.Unauthorized, "another player's token does not open it");
        (await GetCharacterAsync(characterId, adminKey: _integration.Options.AdminKey, path: trace)).Should().Be(HttpStatusCode.Unauthorized, "the content admin key is not a player's token");
        (await GetCharacterAsync(characterId, ownToken, path: trace)).Should().Be(HttpStatusCode.OK);
        (await GetCharacterAsync(characterId, adminToken, path: trace)).Should().Be(HttpStatusCode.OK, "the admin token opens every player");
    }

    [TestMethod]
    public async Task CreatingACharacterForALockedPlayer_NeedsTheirToken()
    {
        await _integration.InitializeElements();
        var (playerName, _) = await LockPlayerAsync("Test Player Dara", "synthetic-pass-4");
        var option = await _integration.GetDriver<CharacterCreationOptionsDriver>().GetDefaultCharacterCreationOptionAsync();
        using var client = _integration.CreateClient();

        var response = await client.PostAsJsonAsync("/api/characters", new CreateCharacterRequest(option.Id, "Not Theirs", PlayerName: playerName), _integration.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task ChangingALockedPlayersPassword_WithoutTheirToken_IsUnauthorized()
    {
        var (playerName, _) = await LockPlayerAsync("Test Player Eryn", "synthetic-pass-5");
        using var client = _integration.CreateClient();

        var response = await client.PutAsJsonAsync("/api/characters/players/password", new SetPlayerPasswordRequest { Name = playerName, Password = "taken-over" }, _integration.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [TestMethod]
    public async Task UnlockingAPlayer_WithTheWrongPassword_IsUnauthorized_AndTheRightOneGivesAToken()
    {
        var (playerName, _) = await LockPlayerAsync("Test Player Fenn", "synthetic-pass-6");
        using var client = _integration.CreateClient();

        var wrong = await client.PostAsJsonAsync("/api/characters/players/unlock", new UnlockPlayerRequest { Name = playerName, Password = "not-the-password" }, _integration.CancellationToken);
        var right = await client.PostAsJsonAsync("/api/characters/players/unlock", new UnlockPlayerRequest { Name = playerName, Password = "synthetic-pass-6" }, _integration.CancellationToken);

        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        right.StatusCode.Should().Be(HttpStatusCode.OK);
        (await right.Content.ReadFromJsonAsync<UnlockPlayerResponse>(_integration.CancellationToken))!.Token.Should().NotBeNullOrEmpty();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // helpers

    private async Task<List<string>> AbilityScoreNamesAsync()
    {
        using var client = _integration.CreateClient();
        var list = await client.GetFromJsonAsync<GetAbilityScoresResponse>("/api/elements/ability-scores", _integration.CancellationToken);
        return list!.Items.Select(i => i.Name).ToList();
    }

    private async Task<string> UnlockAdminAsync(string masterPassword)
    {
        using var client = _integration.CreateClient();
        var response = await client.PostAsJsonAsync("/api/admin/unlock", new AdminUnlockRequest { Password = masterPassword }, _integration.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AdminUnlockResponse>(_integration.CancellationToken))!.Token;
    }

    /// <summary>Locks a new synthetic player with a password (allowed while it has none) and returns its token.</summary>
    private async Task<(string Name, string Token)> LockPlayerAsync(string name, string password)
    {
        using var client = _integration.CreateClient();
        var response = await client.PutAsJsonAsync("/api/characters/players/password", new SetPlayerPasswordRequest { Name = name, Password = password }, _integration.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<UnlockPlayerResponse>(_integration.CancellationToken))!;
        body.Token.Should().NotBeNullOrEmpty();
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

    private async Task<HttpStatusCode> GetCharacterAsync(Guid characterId, string? playerToken = null, string? adminKey = null, string path = "")
    {
        using var client = _integration.CreateClient();
        if (playerToken is not null)
        {
            client.DefaultRequestHeaders.Add(PlayerTokenHeader, playerToken);
        }
        if (adminKey is not null)
        {
            client.DefaultRequestHeaders.Add(IntegrationHost.AdminKeyHeader, adminKey);
        }
        return (await client.GetAsync($"/api/characters/{characterId}{path}", _integration.CancellationToken)).StatusCode;
    }
}
