using System.Net.Http.Json;
using AwesomeAssertions;
using Starlights.Integration.Drivers.Characters;
using Starlights.Integration.Extensions;
using Starlights.Modules.Characters.Endpoints.Characters.SavingThrows.GetSavingThrows;

namespace Starlights.Integration.Drivers.CharacterCreation.Endpoints;

internal sealed class SavingThrowEndpointDriver : IDriver
{
    private readonly IIntegrationHost _integration;
    private readonly CharactersDriverContext _characters;
    public SavingThrowEndpointDriver(IIntegrationHost integration, CharactersDriverContext characters)
    {
        _integration = integration;
        _characters = characters;
    }

    /// <summary>
    /// Retrieves the saving throws for the current character.
    /// </summary>
    public async Task<GetSavingThrowsResponse> GetSavingThrows()
    {
        using var api = _integration.CreateClient();

        var characterId = _characters.CurrentCharacter.Id;
        var url = $"/api/characters/{characterId}/saving-throws";

        var response = await api.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<GetSavingThrowsResponse>();
        data.Should().NotBeNull("Expected response content to be deserializable.");

        return data;
    }
}
