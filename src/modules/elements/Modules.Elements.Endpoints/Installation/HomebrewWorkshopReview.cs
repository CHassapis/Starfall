using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Starlights.Modules.Elements.Domain.Homebrew;
using Starlights.Modules.Elements.Integration;

namespace Starlights.Modules.Elements.Endpoints.Installation;

/// <summary>
/// What saving a Homebrew page edit would do, worked out without saving: what changes (the form's fields, and the
/// elements added, removed or changed), which characters use the record and what they would lose, and what is wrong
/// with it (the same checks the save makes, as diagnostics). Its <see cref="Revision"/> is the record as saved; a save
/// that sends it back as its expected revision is refused if the record changed or went away since.
/// </summary>
/// <param name="Kind">"spells", "subclasses" or "classes".</param>
/// <param name="Id">The id the save would use (the record's, or one made from the name).</param>
/// <param name="IsNew">No record has that id yet.</param>
/// <param name="Revision">The record as saved now; null when there is none.</param>
public sealed record HomebrewEditPreview(
    string Kind,
    string Id,
    bool IsNew,
    string? Revision,
    IReadOnlyList<HomebrewFieldChange> Changes,
    IReadOnlyList<HomebrewElementChange> Elements,
    IReadOnlyList<HomebrewAffectedCharacter> Characters,
    IReadOnlyList<ImportDiagnostic> Diagnostics);

/// <summary>A field of the form that would change (a long text shortened).</summary>
public sealed record HomebrewFieldChange(string Field, string? Before, string? After);

/// <summary>An element of the record (its main element, a feature, a class's multiclass entry) that would change.</summary>
public sealed record HomebrewElementChange(string ElementId, string Name, HomebrewElementChangeKind Change);

[JsonConverter(typeof(JsonStringEnumConverter<HomebrewElementChangeKind>))]
public enum HomebrewElementChangeKind
{
    [JsonStringEnumMemberName("added")] Added,

    [JsonStringEnumMemberName("changed")] Changed,

    /// <summary>Characters that have it lose it when they are next updated.</summary>
    [JsonStringEnumMemberName("removed")] Removed,
}

/// <summary>A character that uses the record: the elements it uses, and those of them the save would remove.</summary>
public sealed record HomebrewAffectedCharacter(Guid CharacterId, string Name, string? PlayerName, IReadOnlyList<string> Uses, IReadOnlyList<string> Loses);

internal static class HomebrewWorkshopReview
{
    // ---- the record as saved ----

    /// <summary>A fingerprint of the record's elements as saved in the file; null when it has none.</summary>
    public static string? Revision(XDocument document, string id)
    {
        var elements = HomebrewWorkshopXml.RecordElements(document.Root!, id).ToList();
        if (elements.Count == 0)
        {
            return null;
        }
        var text = string.Join("\n", elements.Select(e => e.ToString(SaveOptions.DisableFormatting)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    // ---- the checks a save makes, one diagnostic each ----

    private static ImportDiagnostic Invalid(string field, string message) =>
        new(ImportDiagnosticSeverity.Error, ImportDiagnosticCode.InvalidValue, ImportDiagnosticImporter.Homebrew, message) { Detail = field };

    private static void Length(List<ImportDiagnostic> problems, string field, string label, string? text, int max)
    {
        if (!HomebrewWorkshop.Short(text, max))
        {
            problems.Add(Invalid(field, $"{label} can be at most {max:N0} characters."));
        }
    }

    private static void Name(List<ImportDiagnostic> problems, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
        {
            problems.Add(Invalid("name", "It needs a name, of 100 characters at most."));
        }
    }

    private static void Lists(List<ImportDiagnostic> problems, string field, List<string> lists, int max)
    {
        if (lists.Count > max)
        {
            problems.Add(Invalid(field, $"At most {max} spell lists."));
        }
        foreach (var list in lists.Where(l => !HomebrewWorkshop.ListName().IsMatch(l.Trim())))
        {
            problems.Add(Invalid(field, $"The spell list \"{list}\" can only have letters, digits, spaces, apostrophes, brackets and dashes, 50 at most."));
        }
    }

    private static void Features(List<ImportDiagnostic> problems, List<HomebrewFeatureModel> features)
    {
        if (features.Count > 40)
        {
            problems.Add(Invalid("features", "At most 40 features."));
        }
        foreach (var (feature, i) in features.Select((f, i) => (f, i)))
        {
            if (feature.Level is < 1 or > 20 || string.IsNullOrWhiteSpace(feature.Name) || feature.Name.Trim().Length > 100 || !HomebrewWorkshop.Short(feature.Description, 10_000))
            {
                problems.Add(Invalid("features", $"Feature {i + 1}{(string.IsNullOrWhiteSpace(feature.Name) ? "" : $" ({feature.Name.Trim()})")} needs a level from 1 to 20, a name of 100 characters at most and a description of 10,000 at most."));
            }
        }
    }

    private static void Within(List<ImportDiagnostic> problems, string field, string label, IEnumerable<string> values, IReadOnlyCollection<string> allowed)
    {
        foreach (var value in values.Where(v => !allowed.Contains(v)))
        {
            problems.Add(Invalid(field, $"{label}: \"{value}\" is not one of {string.Join(", ", allowed)}."));
        }
    }

    /// <summary>What the save of a spell refuses; empty when it saves.</summary>
    public static List<ImportDiagnostic> Spell(HomebrewSpellModel m)
    {
        var problems = new List<ImportDiagnostic>();
        Name(problems, m.Name);
        if (m.Level is < 0 or > 9)
        {
            problems.Add(Invalid("level", "The level is 0 (a cantrip) to 9."));
        }
        if (!HomebrewWorkshopXml.Schools.Contains(m.School))
        {
            problems.Add(Invalid("school", $"The school is one of {string.Join(", ", HomebrewWorkshopXml.Schools)}."));
        }
        Length(problems, "source", "The book", m.Source, 100);
        Length(problems, "castingTime", "The casting time", m.CastingTime, 100);
        Length(problems, "range", "The range", m.Range, 100);
        Length(problems, "duration", "The duration", m.Duration, 100);
        Length(problems, "materialText", "The material component", m.MaterialText, 300);
        Length(problems, "description", "What it does", m.Description, 20_000);
        Length(problems, "higherLevels", "At higher levels", m.HigherLevels, 5_000);
        Lists(problems, "lists", m.Lists, 20);
        return problems;
    }

    /// <summary>What the save of a subclass refuses; empty when it saves.</summary>
    public static List<ImportDiagnostic> Subclass(HomebrewSubclassModel m, IEnumerable<SubclassTarget> targets)
    {
        var problems = new List<ImportDiagnostic>();
        Name(problems, m.Name);
        Length(problems, "source", "The book", m.Source, 100);
        Length(problems, "description", "The description", m.Description, 20_000);
        if (!targets.Any(t => t.Supports == m.ClassSupports))
        {
            problems.Add(Invalid("classSupports", "Choose the class whose subclass choice it joins."));
        }
        Features(problems, m.Features);
        return problems;
    }

    /// <summary>What the save of a class refuses; empty when it saves.</summary>
    public static List<ImportDiagnostic> Class(HomebrewClassModel m)
    {
        var problems = new List<ImportDiagnostic>();
        if (string.IsNullOrWhiteSpace(m.Name) || !HomebrewWorkshop.ListName().IsMatch(m.Name.Trim()))
        {
            problems.Add(Invalid("name", "It needs a name of letters, digits, spaces, apostrophes, brackets and dashes, 50 at most."));
        }
        Length(problems, "source", "The book", m.Source, 100);
        Length(problems, "description", "The description", m.Description, 20_000);
        if (m.HitDie is not (6 or 8 or 10 or 12))
        {
            problems.Add(Invalid("hitDie", "The hit die is a d6, d8, d10 or d12."));
        }
        Within(problems, "savingThrows", "Saving throws", m.SavingThrows, HomebrewWorkshopXml.Abilities);
        if (m.SavingThrows.Count > 3)
        {
            problems.Add(Invalid("savingThrows", "At most 3 saving throws."));
        }
        Within(problems, "armor", "Armor", m.Armor, HomebrewWorkshopXml.ArmorKinds);
        Within(problems, "weapons", "Weapons", m.Weapons, HomebrewWorkshopXml.WeaponKinds);
        Within(problems, "multiclassArmor", "Multiclass armor (of the class's own)", m.MulticlassArmor, m.Armor);
        Within(problems, "multiclassWeapons", "Multiclass weapons (of the class's own)", m.MulticlassWeapons, m.Weapons);
        Within(problems, "skills", "Skills", m.Skills, HomebrewWorkshopXml.Skills);
        if (m.SkillChoices is < 0 or > 6)
        {
            problems.Add(Invalid("skillChoices", "From 0 to 6 skill choices."));
        }
        if (m.MulticlassAbility is { Length: > 0 } ability && !HomebrewWorkshopXml.Abilities.Contains(ability))
        {
            problems.Add(Invalid("multiclassAbility", $"The multiclass ability is one of {string.Join(", ", HomebrewWorkshopXml.Abilities)}."));
        }
        if (m.SubclassLevel is < 1 or > 20)
        {
            problems.Add(Invalid("subclassLevel", "The subclass comes at a level from 1 to 20."));
        }
        if (m.AsiLevels.Any(l => l is < 1 or > 20))
        {
            problems.Add(Invalid("asiLevels", "Ability Score Improvements come at levels from 1 to 20."));
        }
        if (!HomebrewWorkshopXml.CastingKinds.Contains(m.Casting))
        {
            problems.Add(Invalid("casting", $"Spellcasting is one of {string.Join(", ", HomebrewWorkshopXml.CastingKinds)}."));
        }
        if (m.Casting != "None" && !HomebrewWorkshopXml.Abilities.Contains(m.CastingAbility ?? string.Empty))
        {
            problems.Add(Invalid("castingAbility", "A class that casts needs its spellcasting ability."));
        }
        Lists(problems, "spellLists", m.SpellLists, 10);
        if (m.Cantrips is < 0 or > 6)
        {
            problems.Add(Invalid("cantrips", "From 0 to 6 cantrips."));
        }
        Features(problems, m.Features);
        return problems;
    }

    // ---- what a save would change ----

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // what the form shows as its own parts, not fields
    private static readonly string[] NotFields = ["id", "revision", "expectedRevision", "features"];

    /// <summary>
    /// The preview. <paramref name="before"/> is the record as saved (null when new), <paramref name="after"/> and
    /// <paramref name="record"/> what the save would write (null when the checks refuse it).
    /// </summary>
    public static async Task<HomebrewEditPreview> BuildAsync<T>(string kind, XDocument document, string id, T? before, T? after, IReadOnlyList<XElement>? record,
        List<ImportDiagnostic> diagnostics, IElementUsage usage, CancellationToken ct) where T : class
    {
        var old = Parts(HomebrewWorkshopXml.RecordElements(document.Root!, id));
        var next = record is null ? null : Parts(record);

        var elements = new List<HomebrewElementChange>();
        if (next is not null)
        {
            foreach (var (elementId, part) in next)
            {
                if (!old.TryGetValue(elementId, out var was))
                {
                    elements.Add(new HomebrewElementChange(elementId, part.Name, HomebrewElementChangeKind.Added));
                }
                else if (was.Xml != part.Xml)
                {
                    elements.Add(new HomebrewElementChange(elementId, part.Name, HomebrewElementChangeKind.Changed));
                }
            }
            elements.AddRange(old.Where(o => !next.ContainsKey(o.Key)).Select(o => new HomebrewElementChange(o.Key, o.Value.Name, HomebrewElementChangeKind.Removed)));
        }
        var removed = elements.Where(e => e.Change == HomebrewElementChangeKind.Removed).Select(e => e.ElementId).ToHashSet(StringComparer.Ordinal);

        var byGuid = old.Keys.Concat(next?.Keys.AsEnumerable() ?? []).Distinct(StringComparer.Ordinal).ToDictionary(AuroraIds.ToGuid);
        var characters = (await usage.GetUsersAsync(byGuid.Keys, ct))
            .Select(u =>
            {
                var uses = u.ElementIds.Select(g => byGuid[g]).Order(StringComparer.Ordinal).ToList();
                return new HomebrewAffectedCharacter(u.CharacterId, u.CharacterName, u.PlayerName, uses, [.. uses.Where(removed.Contains)]);
            })
            .ToList();

        return new HomebrewEditPreview(kind, id, old.Count == 0, Revision(document, id), before is null || after is null ? [] : Fields(before, after),
            [.. elements.OrderBy(e => e.Change).ThenBy(e => e.ElementId, StringComparer.Ordinal)], characters, ImportDiagnostic.Arrange(diagnostics));
    }

    /// <summary>The elements a record imports as, by Aurora id: each element, and a class's multiclass entry (an element of its own once imported).</summary>
    private static Dictionary<string, (string Name, string Xml)> Parts(IEnumerable<XElement> record)
    {
        var parts = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var e in record)
        {
            var name = (string?)e.Attribute("name") ?? string.Empty;
            if ((string?)e.Attribute("id") is { } id)
            {
                parts[id] = (name, e.ToString(SaveOptions.DisableFormatting));
            }
            foreach (var multiclass in e.Elements("multiclass"))
            {
                if ((string?)multiclass.Attribute("id") is { } multiclassId)
                {
                    parts[multiclassId] = ($"{name} (multiclass)", multiclass.ToString(SaveOptions.DisableFormatting));
                }
            }
        }
        return parts;
    }

    private static List<HomebrewFieldChange> Fields<T>(T before, T after)
    {
        var was = JsonSerializer.SerializeToNode(before, Json)!.AsObject();
        var now = JsonSerializer.SerializeToNode(after, Json)!.AsObject();
        return now
            .Where(p => !NotFields.Contains(p.Key) && !JsonNode.DeepEquals(was[p.Key], p.Value))
            .Select(p => new HomebrewFieldChange(p.Key, Show(was[p.Key]), Show(p.Value)))
            .ToList();
    }

    private static string? Show(JsonNode? value)
    {
        var text = value switch
        {
            null => null,
            JsonArray list => string.Join(", ", list.Select(i => i?.ToString())),
            JsonValue v when v.TryGetValue<bool>(out var b) => b ? "yes" : "no",
            _ => value.ToString(),
        };
        return text is { Length: > 160 } ? text[..157] + "…" : text;
    }

    /// <summary>A new record whose name gives the id of one that is saved: the save would take its place.</summary>
    public static ImportDiagnostic ReplacesExisting(string id, string name) =>
        new(ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.ReplacesExisting, ImportDiagnosticImporter.Homebrew,
            $"There is already a {name} ({id}); saving this as new would take its place. Edit that one instead, or choose another name.")
        { ElementId = id, ElementName = name };

    /// <summary>The record changed (or was removed) after the preview a save was based on.</summary>
    public static ImportDiagnostic Stale(string id, string? current) =>
        new(ImportDiagnosticSeverity.Error, ImportDiagnosticCode.StaleRevision, ImportDiagnosticImporter.Homebrew,
            current is null ? "It was removed after you opened it, so it was not saved." : "Someone saved it after you opened it, so it was not saved. Check what changed below, then save again.")
        { ElementId = id };
}
