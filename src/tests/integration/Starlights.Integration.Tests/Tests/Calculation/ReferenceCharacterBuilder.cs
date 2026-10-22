using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Starlights.Modules.Characters.Data.EntityFramework;
using Starlights.Platform.Components.Data.EntityFramework;

namespace Starlights.Integration.Tests.Calculation;

/// <summary>
/// Builds a reference character (src/frontend/apps/builder-app/reference-characters) through the API, step by step
/// as a player would: ability scores, the builder's choices, levels, a second class, the inventory, story fields, and
/// a round trip through the Starfall character file.
/// </summary>
internal sealed class ReferenceCharacterBuilder(IntegrationHost integration, HttpClient api)
{
    private readonly Dictionary<string, Guid> _elementIds = new(StringComparer.Ordinal);

    public Guid CharacterId { get; private set; }

    public async Task CreateAsync(string name)
    {
        var options = await GetAsync("/api/characters/creation-options");
        var optionId = options["options"]![0]!["id"]!.GetValue<Guid>();
        var created = await api.PostAsJsonAsync("/api/characters", new { CharacterCreationOptionId = optionId, Name = name, PlayerName = "Reference" });
        await EnsureSuccess(created, "create the character");
        CharacterId = (await ReadAsync(created))["id"]!.GetValue<Guid>();
        await SettleAsync();
    }

    public async Task ApplyAsync(JsonNode step)
    {
        if (step["abilities"] is JsonObject abilities)
        {
            await SetAbilitiesAsync(abilities);
        }
        else if (step["pick"] is not null)
        {
            await PickAsync(step["pick"]!.GetValue<string>(), step["option"]!.GetValue<string>());
        }
        else if (step["level"] is not null)
        {
            await LevelAsync(step["level"]!.GetValue<string>(), step["to"]!.GetValue<int>());
        }
        else if (step["multiclass"] is not null)
        {
            await MulticlassAsync(step["multiclass"]!.GetValue<string>());
        }
        else if (step["inventory"] is JsonArray items)
        {
            await SetInventoryAsync(items, step["customAttacks"] as JsonArray);
        }
        else if (step["story"] is JsonObject story)
        {
            await SetStoryAsync(story);
        }
        else if (step["exportImport"]?.GetValue<bool>() == true)
        {
            await ExportAndImportAsync();
        }
        else
        {
            throw new InvalidOperationException($"Unknown step {step.ToJsonString()}");
        }
        await SettleAsync();
    }

    public Task<JsonNode> GetCharacterAsync(string path = "") => GetAsync($"/api/characters/{CharacterId}{path}");

    /// <summary>Starfall element ids of Aurora ids (the reference content's own ids).</summary>
    public async Task<Guid> ElementIdAsync(string auroraId)
    {
        if (!_elementIds.ContainsKey(auroraId))
        {
            var response = await api.PostAsJsonAsync("/api/elements/aurora-lookup", new { Ids = new[] { auroraId } });
            await EnsureSuccess(response, $"look up {auroraId}");
            var found = (await ReadAsync(response))["elements"]![auroraId]
                ?? throw new AssertFailedException($"{auroraId} is not in the imported reference content");
            _elementIds[auroraId] = found.GetValue<Guid>();
        }
        return _elementIds[auroraId];
    }

    /// <summary>Compendium entries (with their Aurora ids and setters) of element ids.</summary>
    public async Task<Dictionary<Guid, JsonNode>> EntriesAsync(IEnumerable<Guid> elementIds)
    {
        var entries = new Dictionary<Guid, JsonNode>();
        foreach (var chunk in elementIds.Distinct().Chunk(60))
        {
            var batch = await GetAsync($"/api/elements/compendium/batch?ids={string.Join(",", chunk)}");
            foreach (var entry in batch["entries"]!.AsArray())
            {
                entries[entry!["id"]!.GetValue<Guid>()] = entry;
            }
        }
        return entries;
    }

    private async Task SetAbilitiesAsync(JsonObject abilities)
    {
        var scores = (await GetCharacterAsync("/ability-scores"))["abilityScores"]!.AsArray();
        foreach (var (name, value) in abilities)
        {
            var score = scores.First(s => s!["name"]!.GetValue<string>() == name)!;
            var response = await api.PostAsJsonAsync($"/api/characters/{CharacterId}/ability-scores/{score["abilityScoreId"]}/base", new { Value = value!.GetValue<int>() });
            await EnsureSuccess(response, $"set {name}");
        }
    }

    // the first open choice of that name (or the top-level Class/Species/Background), then its option by name
    private async Task PickAsync(string choiceName, string optionName)
    {
        var choices = (await GetCharacterAsync("/builder/choices"))["choices"]!.AsArray();
        var choice = choices.FirstOrDefault(c => c!["selected"] is null &&
                (c["name"]!.GetValue<string>() == choiceName || (c["depth"]!.GetValue<int>() == 0 && c["section"]!.GetValue<string>() == choiceName)))
            ?? throw new AssertFailedException($"No open choice \"{choiceName}\"; the choices are: {string.Join(", ", choices.Select(c => $"{c!["name"]} ({c["section"]}, {(c["selected"] is null ? "open" : "taken")})"))}");
        var ruleId = choice["ruleId"]!.GetValue<Guid>();
        var options = (await GetCharacterAsync($"/builder/selection-rules/{ruleId}/options"))["options"]!.AsArray();
        var option = options.FirstOrDefault(o => o!["name"]!.GetValue<string>() == optionName)
            ?? throw new AssertFailedException($"\"{choiceName}\" offers no \"{optionName}\"; it offers: {string.Join(", ", options.Select(o => o!["name"]))}");
        var response = await api.PostAsJsonAsync($"/api/characters/{CharacterId}/builder/selection-rules/{ruleId}/register",
            new { ParentRegistration = choice["registrationId"]!.GetValue<Guid>(), ElementId = option["elementId"]!.GetValue<Guid>() });
        await EnsureSuccess(response, $"pick {optionName} for {choiceName}");
    }

    private async Task LevelAsync(string className, int level)
    {
        var classes = (await GetCharacterAsync("/classes"))["classes"]!.AsArray();
        var found = classes.FirstOrDefault(c => c!["name"]!.GetValue<string>() == className)
            ?? throw new AssertFailedException($"The character has no class {className}");
        var response = await api.PostAsJsonAsync($"/api/characters/{CharacterId}/classes/{found["characterClassId"]}/level", new { NewLevel = level });
        await EnsureSuccess(response, $"level {className} to {level}");
    }

    private async Task MulticlassAsync(string className)
    {
        var options = (await GetCharacterAsync("/multiclass"))["options"]!.AsArray();
        var option = options.FirstOrDefault(o => o!["name"]!.GetValue<string>() == className)
            ?? throw new AssertFailedException($"{className} is not offered as a second class; offered: {string.Join(", ", options.Select(o => o!["name"]))}");
        var response = await api.PostAsJsonAsync($"/api/characters/{CharacterId}/multiclass", new { ElementId = option["elementId"]!.GetValue<Guid>() });
        await EnsureSuccess(response, $"take {className} as a second class");
    }

    private async Task SetInventoryAsync(JsonArray items, JsonArray? customAttacks)
    {
        var current = (JsonObject)await GetCharacterAsync("/inventory");
        var entries = new JsonArray();
        foreach (var item in items)
        {
            var entry = new JsonObject
            {
                ["id"] = item!["id"]!.GetValue<string>(),
                ["elementId"] = await ElementIdAsync(item["item"]!.GetValue<string>()),
                ["quantity"] = 1,
            };
            if (item["base"] is not null)
            {
                entry["baseElementId"] = await ElementIdAsync(item["base"]!.GetValue<string>());
            }
            foreach (var field in new[] { "equipped", "attuned", "attack" })
            {
                if (item[field] is not null)
                {
                    entry[field] = item[field]!.DeepClone();
                }
            }
            entries.Add(entry);
        }
        current["items"] = entries;
        if (customAttacks is not null)
        {
            current["customAttacks"] = customAttacks.DeepClone();
        }
        var response = await api.PutAsJsonAsync($"/api/characters/{CharacterId}/inventory", current);
        await EnsureSuccess(response, "save the inventory");
    }

    private async Task SetStoryAsync(JsonObject story)
    {
        var fields = (JsonObject)((await GetCharacterAsync("/story"))["fields"] ?? new JsonObject()).DeepClone();
        foreach (var (key, value) in story)
        {
            fields[key] = value!.DeepClone();
        }
        var response = await api.PutAsJsonAsync($"/api/characters/{CharacterId}/story", new JsonObject { ["fields"] = fields });
        await EnsureSuccess(response, "save the story");
    }

    private async Task ExportAndImportAsync()
    {
        var file = await GetCharacterAsync("/export");
        var response = await api.PostAsJsonAsync("/api/characters/import", new JsonObject { ["file"] = file.DeepClone(), ["playerName"] = "Reference", ["confirm"] = true });
        await EnsureSuccess(response, "import the exported character");
        CharacterId = (await ReadAsync(response))["characterId"]!.GetValue<Guid>();
    }

    /// <summary>
    /// Waits until the character's changes are worked out: the builder has nothing pending and the characters'
    /// outbox (which reprocesses a character after a level change) is empty, twice in a row.
    /// </summary>
    public async Task SettleAsync()
    {
        var quiet = 0;
        for (var i = 0; i < 600 && quiet < 2; i++)
        {
            var pending = (await GetCharacterAsync("/builder/choices"))["pending"]!.GetValue<bool>();
            using var scope = integration.Services.CreateScope();
            var events = scope.ServiceProvider.GetRequiredService<CharactersContext>().Set<EventMessage>();
            var failed = await events.Where(m => m.ErrorMessage != null).Select(m => m.EventType + ": " + m.ErrorMessage).FirstOrDefaultAsync();
            if (failed is not null)
            {
                throw new AssertFailedException($"Processing a character event failed: {failed}");
            }
            var waiting = await events.AnyAsync(m => m.ProcessedOn == null);
            quiet = pending || waiting ? 0 : quiet + 1;
            await Task.Delay(quiet == 0 ? 50 : 100);
        }
        if (quiet < 2)
        {
            throw new TimeoutException("The character's processing did not settle.");
        }
    }

    private async Task<JsonNode> GetAsync(string url)
    {
        var response = await api.GetAsync(url);
        await EnsureSuccess(response, $"GET {url}");
        return await ReadAsync(response);
    }

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync()) ?? throw new AssertFailedException("Empty response");

    private static async Task EnsureSuccess(HttpResponseMessage response, string what)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new AssertFailedException($"Could not {what}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
    }
}
