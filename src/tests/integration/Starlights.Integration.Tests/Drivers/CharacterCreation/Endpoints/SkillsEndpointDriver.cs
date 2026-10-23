using System.Net.Http.Json;
using AwesomeAssertions;
using Starlights.Integration.Drivers.Characters;
using Starlights.Integration.Extensions;
using Starlights.Modules.Characters.Endpoints.Characters.Skills.GetSkills;

namespace Starlights.Integration.Drivers.CharacterCreation.Endpoints;

internal sealed class SkillsEndpointDriver : IDriver
{
    private readonly IIntegrationHost _integration;
    private readonly CharactersDriverContext _characters;

    public SkillsEndpointDriver(IIntegrationHost integration, CharactersDriverContext characters)
    {
        _integration = integration;
        _characters = characters;
    }

    /// <summary>
    /// Retrieves the skills for the current character.
    /// </summary>
    public async Task<GetSkillsResponse> GetSkills()
    {
        using var api = _integration.CreateClient();

        var characterId = _characters.CurrentCharacter.Id;
        var url = $"/api/characters/{characterId}/skills";

        var response = await api.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<GetSkillsResponse>();
        data.Should().NotBeNull("Expected response content to be deserializable.");

        return data;
    }
}
