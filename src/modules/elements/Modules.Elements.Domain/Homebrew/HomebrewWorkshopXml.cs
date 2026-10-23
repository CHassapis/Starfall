using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Starlights.Modules.Elements.Domain.Homebrew;

/// <summary>A feature a homebrew class or subclass gains at a level.</summary>
public sealed record HomebrewFeatureModel
{
    /// <summary>Kept across edits so characters keep the feature (empty: made from the name).</summary>
    public string? Id { get; init; }
    public int Level { get; init; } = 1;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

/// <summary>A homebrew spell as the form edits it.</summary>
public sealed record HomebrewSpellModel
{
    public string? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Source { get; init; }
    /// <summary>0 is a cantrip.</summary>
    public int Level { get; init; }
    public string School { get; init; } = "Evocation";
    public string CastingTime { get; init; } = "1 action";
    public string Range { get; init; } = "Self";
    public string Duration { get; init; } = "Instantaneous";
    public bool Verbal { get; init; } = true;
    public bool Somatic { get; init; } = true;
    public bool Material { get; init; }
    public string? MaterialText { get; init; }
    public bool Concentration { get; init; }
    public bool Ritual { get; init; }
    /// <summary>The class spell lists it is on ("Wizard", "Cleric", a homebrew class's name).</summary>
    public List<string> Lists { get; init; } = [];
    /// <summary>What it does: plain text, paragraphs separated by blank lines.</summary>
    public string Description { get; init; } = string.Empty;
    public string? HigherLevels { get; init; }

    // ---- what it rolls, said outright (optional): read instead of the text, so the numbers never depend on wording ----
    // (the fields follow Forge Character Builder's custom spell mechanics, github.com/tommyruin/forge-character-builder, MIT)

    /// <summary>
    /// What it rolls: "melee-attack", "ranged-attack", "save", or "none" (nothing to roll: healing, a utility spell).
    /// Empty: not said, and the simulator reads the spell's text as for any book spell.
    /// </summary>
    public string? Roll { get; init; }
    /// <summary>A "save" spell: the ability the target saves with.</summary>
    public string? SaveAbility { get; init; }
    /// <summary>A "save" spell: a successful save still takes half the damage (else none).</summary>
    public bool HalfOnSave { get; init; }
    /// <summary>Its damage, terms separated by ";": "1d10 cold", "2d6 fire; 1d6 radiant".</summary>
    public string? Damage { get; init; }
    /// <summary>The hit points it restores: "2d8".</summary>
    public string? Healing { get; init; }
    /// <summary>The spellcasting ability modifier joins the damage (its first term) or the healing.</summary>
    public bool AddsModifier { get; init; }
    /// <summary>A cantrip: from a character level on, the first damage term (or the healing) rolls these dice: "5:2d10; 11:3d10; 17:4d10".</summary>
    public string? CantripScaling { get; init; }
    /// <summary>A leveled spell: the dice each slot level above its own adds to the first damage term (or the healing): "1d10".</summary>
    public string? SlotScaling { get; init; }

    /// <summary>Set when read: the record as saved. A save that sends it back as <see cref="ExpectedRevision"/> is refused if the record changed since.</summary>
    public string? Revision { get; init; }

    /// <summary>On a save: the revision the edit started from (a preview's); a record changed or removed since is refused (409).</summary>
    public string? ExpectedRevision { get; init; }
}

/// <summary>A homebrew subclass: which class's subclass choice it answers, and its features.</summary>
public sealed record HomebrewSubclassModel
{
    public string? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Source { get; init; }
    /// <summary>The class's subclass choice it appears in (its supports, e.g. "Fighter Subclass").</summary>
    public string ClassSupports { get; init; } = string.Empty;
    /// <summary>The class's name, for showing (e.g. "Fighter (2024)").</summary>
    public string ClassName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<HomebrewFeatureModel> Features { get; init; } = [];
    /// <summary>Set when read: the record as saved. A save that sends it back as <see cref="ExpectedRevision"/> is refused if the record changed since.</summary>
    public string? Revision { get; init; }

    /// <summary>On a save: the revision the edit started from (a preview's); a record changed or removed since is refused (409).</summary>
    public string? ExpectedRevision { get; init; }
}

/// <summary>A homebrew class.</summary>
public sealed record HomebrewClassModel
{
    public string? Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Source { get; init; }
    public string Description { get; init; } = string.Empty;
    /// <summary>6, 8, 10 or 12.</summary>
    public int HitDie { get; init; } = 8;
    /// <summary>Two abilities ("Strength", "Wisdom").</summary>
    public List<string> SavingThrows { get; init; } = [];
    /// <summary>Light, Medium, Heavy, Shields.</summary>
    public List<string> Armor { get; init; } = [];
    /// <summary>Simple, Martial.</summary>
    public List<string> Weapons { get; init; } = [];
    public int SkillChoices { get; init; } = 2;
    /// <summary>The skills to choose from ("Arcana", "Sleight of Hand").</summary>
    public List<string> Skills { get; init; } = [];
    /// <summary>What a character multiclassing into it gets (a subset of Armor and Weapons); one skill if it has choices.</summary>
    public List<string> MulticlassArmor { get; init; } = [];
    public List<string> MulticlassWeapons { get; init; } = [];
    /// <summary>The ability that must be 13 to multiclass into it (empty: none).</summary>
    public string? MulticlassAbility { get; init; }
    public int SubclassLevel { get; init; } = 3;
    /// <summary>Its subclasses' supports; set when the class is made and kept when it is renamed.</summary>
    public string? SubclassSupports { get; init; }
    public List<int> AsiLevels { get; init; } = [4, 8, 12, 16];
    public bool EpicBoon { get; init; } = true;
    public List<HomebrewFeatureModel> Features { get; init; } = [];
    /// <summary>None, Full (prepares, like the 2024 Cleric), Half (learns, like the Paladin) or Pact (like the Warlock).</summary>
    public string Casting { get; init; } = "None";
    public string? CastingAbility { get; init; }
    /// <summary>The spell lists it casts from besides its own (e.g. "Wizard"): its own list starts empty.</summary>
    public List<string> SpellLists { get; init; } = [];
    /// <summary>Cantrips at level 1 (one more at 4 and 10); Half casters have none.</summary>
    public int Cantrips { get; init; } = 3;
    /// <summary>Its spell list's name; set when the class is made and kept when it is renamed.</summary>
    public string? SpellListName { get; init; }
    /// <summary>Set when read: the record as saved. A save that sends it back as <see cref="ExpectedRevision"/> is refused if the record changed since.</summary>
    public string? Revision { get; init; }

    /// <summary>On a save: the revision the edit started from (a preview's); a record changed or removed since is refused (409).</summary>
    public string? ExpectedRevision { get; init; }
}

/// <summary>A class whose subclass choice a homebrew subclass can join.</summary>
public sealed record SubclassTarget(string ClassName, string? Source, string Supports);

/// <summary>
/// Turns the Homebrew page's spells, subclasses and classes into Aurora elements (and back), built like the 2024
/// Player's Handbook's own: a spell's setters; a subclass (Archetype) whose supports match its class's subclass
/// choice, with its features granted by level; a class with proficiencies (those a multiclass character doesn't get
/// carry the multiclass requirement), features by level, the subclass choice, Ability Score Improvement and Epic
/// Boon feats, and optional spellcasting with the 2024 Cleric's, Paladin's or Warlock's tables.
/// </summary>
public static partial class HomebrewWorkshopXml
{
    public static readonly string[] Schools = ["Abjuration", "Conjuration", "Divination", "Enchantment", "Evocation", "Illusion", "Necromancy", "Transmutation"];
    public static readonly string[] Abilities = ["Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma"];
    public static readonly string[] SpellRolls = ["none", "melee-attack", "ranged-attack", "save"];
    public static readonly string[] DamageTypes = ["acid", "bludgeoning", "cold", "fire", "force", "lightning", "necrotic", "piercing", "poison", "psychic", "radiant", "slashing", "thunder"];

    /// <summary>
    /// The setters a spell's mechanics are written as (one each, so the compendium's setter map keeps them apart; several
    /// values in one are separated by ";"). Only written when the spell says what it rolls.
    /// </summary>
    public static class SpellSetters
    {
        public const string Roll = "starlights-spell-roll", Save = "starlights-spell-save", SaveSuccess = "starlights-spell-save-success",
            Damage = "starlights-spell-damage", Healing = "starlights-spell-healing", Modifier = "starlights-spell-modifier",
            CantripScaling = "starlights-spell-cantrip-scaling", SlotScaling = "starlights-spell-slot-scaling";
    }
    public static readonly string[] Skills =
    [
        "Acrobatics", "Animal Handling", "Arcana", "Athletics", "Deception", "History", "Insight", "Intimidation", "Investigation",
        "Medicine", "Nature", "Perception", "Performance", "Persuasion", "Religion", "Sleight of Hand", "Stealth", "Survival",
    ];
    public static readonly string[] ArmorKinds = ["Light", "Medium", "Heavy", "Shields"];
    public static readonly string[] WeaponKinds = ["Simple", "Martial"];
    public static readonly string[] CastingKinds = ["None", "Full", "Half", "Pact"];

    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex NotIdChar();

    public static string Slug(string text) => NotIdChar().Replace(text.ToUpperInvariant(), "_").Trim('_');

    public static string IdFor(string kind, string name) => $"ID_STARLIGHTS_HOMEBREW_{kind}_{Slug(name)}";

    private static IEnumerable<XElement> Paragraphs(string text, string fallback)
    {
        var paragraphs = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return paragraphs.Length == 0 ? [new XElement("p", fallback)] : paragraphs.Select(p => new XElement("p", p));
    }

    private static string Text(XElement? description) =>
        description is null ? string.Empty : string.Join("\n\n", description.Elements("p").Select(p => p.Value.Trim()).Where(p => p.Length > 0));

    private static string Source(string? source) => string.IsNullOrWhiteSpace(source) ? "Homebrew" : source.Trim();

    private static XElement? Set(XElement e, string name) => e.Element("setters")?.Elements("set").FirstOrDefault(s => (string?)s.Attribute("name") == name);

    private static bool Flag(XElement e, string name) => string.Equals(Set(e, name)?.Value, "true", StringComparison.OrdinalIgnoreCase);

    // ---- spells ----

    public static XElement SpellToXml(HomebrewSpellModel m, string id)
    {
        var lists = m.Lists.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var description = new XElement("description", Paragraphs(m.Description, m.Name.Trim()));
        if (!string.IsNullOrWhiteSpace(m.HigherLevels))
        {
            description.Add(new XElement("p", new XAttribute("class", "indent"), new XElement("strong", new XElement("em", "At Higher Levels.")), " " + m.HigherLevels.Trim()));
        }
        return new XElement("element",
            new XAttribute("name", m.Name.Trim()),
            new XAttribute("type", "Spell"),
            new XAttribute("source", Source(m.Source)),
            new XAttribute("id", id),
            new XElement("supports", string.Join(", ", lists)),
            description,
            new XElement("setters",
                new XElement("set", new XAttribute("name", "level"), m.Level),
                new XElement("set", new XAttribute("name", "school"), m.School),
                new XElement("set", new XAttribute("name", "time"), m.CastingTime.Trim()),
                new XElement("set", new XAttribute("name", "duration"), m.Duration.Trim()),
                new XElement("set", new XAttribute("name", "range"), m.Range.Trim()),
                new XElement("set", new XAttribute("name", "hasVerbalComponent"), m.Verbal ? "true" : "false"),
                new XElement("set", new XAttribute("name", "hasSomaticComponent"), m.Somatic ? "true" : "false"),
                new XElement("set", new XAttribute("name", "hasMaterialComponent"), m.Material ? "true" : "false"),
                new XElement("set", new XAttribute("name", "materialComponent"), m.Material ? m.MaterialText?.Trim() ?? string.Empty : string.Empty),
                new XElement("set", new XAttribute("name", "isConcentration"), m.Concentration ? "true" : "false"),
                new XElement("set", new XAttribute("name", "isRitual"), m.Ritual ? "true" : "false"),
                SpellMechanicsXml(m)));
    }

    /// <summary>The mechanics setters, when the spell says what it rolls; nothing otherwise (a spell saved before is written as it was).</summary>
    private static IEnumerable<XElement> SpellMechanicsXml(HomebrewSpellModel m)
    {
        if (string.IsNullOrWhiteSpace(m.Roll))
        {
            yield break;
        }
        XElement Setter(string name, string value) => new("set", new XAttribute("name", name), value);
        var roll = m.Roll.Trim();
        yield return Setter(SpellSetters.Roll, roll);
        if (roll == "save" && !string.IsNullOrWhiteSpace(m.SaveAbility))
        {
            yield return Setter(SpellSetters.Save, m.SaveAbility.Trim());
            yield return Setter(SpellSetters.SaveSuccess, m.HalfOnSave ? "half" : "none");
        }
        if (Terms(m.Damage) is { Count: > 0 } damage)
        {
            yield return Setter(SpellSetters.Damage, string.Join("; ", damage));
        }
        if (!string.IsNullOrWhiteSpace(m.Healing))
        {
            yield return Setter(SpellSetters.Healing, m.Healing.Trim());
        }
        if (m.AddsModifier)
        {
            yield return Setter(SpellSetters.Modifier, "true");
        }
        if (m.Level == 0 && Terms(m.CantripScaling) is { Count: > 0 } tiers)
        {
            yield return Setter(SpellSetters.CantripScaling, string.Join("; ", tiers));
        }
        if (m.Level > 0 && !string.IsNullOrWhiteSpace(m.SlotScaling))
        {
            yield return Setter(SpellSetters.SlotScaling, m.SlotScaling.Trim());
        }
    }

    /// <summary>The terms of a ";"-separated value, trimmed, empty ones left out.</summary>
    public static List<string> Terms(string? value) =>
        (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static HomebrewSpellModel SpellFromXml(XElement e)
    {
        var description = e.Element("description");
        var higher = description?.Elements("p").FirstOrDefault(p => p.Value.TrimStart().StartsWith("At Higher Levels.", StringComparison.Ordinal));
        var paragraphs = description?.Elements("p").Where(p => p != higher).Select(p => p.Value.Trim()) ?? [];
        return new HomebrewSpellModel
        {
            Id = (string?)e.Attribute("id"),
            Name = (string?)e.Attribute("name") ?? string.Empty,
            Source = (string?)e.Attribute("source"),
            Level = int.TryParse(Set(e, "level")?.Value, out var level) ? level : 0,
            School = Set(e, "school")?.Value ?? "Evocation",
            CastingTime = Set(e, "time")?.Value ?? string.Empty,
            Range = Set(e, "range")?.Value ?? string.Empty,
            Duration = Set(e, "duration")?.Value ?? string.Empty,
            Verbal = Flag(e, "hasVerbalComponent"),
            Somatic = Flag(e, "hasSomaticComponent"),
            Material = Flag(e, "hasMaterialComponent"),
            MaterialText = Set(e, "materialComponent")?.Value is { Length: > 0 } material ? material : null,
            Concentration = Flag(e, "isConcentration"),
            Ritual = Flag(e, "isRitual"),
            Lists = (e.Element("supports")?.Value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Description = string.Join("\n\n", paragraphs.Where(p => p.Length > 0)),
            HigherLevels = higher?.Value.Trim()["At Higher Levels.".Length..].Trim(),
            Roll = Set(e, SpellSetters.Roll)?.Value,
            SaveAbility = Set(e, SpellSetters.Save)?.Value,
            HalfOnSave = Set(e, SpellSetters.SaveSuccess)?.Value == "half",
            Damage = Set(e, SpellSetters.Damage)?.Value,
            Healing = Set(e, SpellSetters.Healing)?.Value,
            AddsModifier = Flag(e, SpellSetters.Modifier),
            CantripScaling = Set(e, SpellSetters.CantripScaling)?.Value,
            SlotScaling = Set(e, SpellSetters.SlotScaling)?.Value,
        };
    }

    // ---- features (shared by subclasses and classes) ----

    /// <summary>Each feature's id: the one it had, else made from its owner and name (unique within the owner).</summary>
    private static List<(HomebrewFeatureModel Feature, string Id)> FeatureIds(string ownerId, IEnumerable<HomebrewFeatureModel> features)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<(HomebrewFeatureModel, string)>();
        foreach (var f in features.OrderBy(f => f.Level).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var id = f.Id is { } kept && kept.StartsWith(ownerId + "_F_", StringComparison.Ordinal) ? kept : $"{ownerId}_F_{Slug(f.Name)}";
            if (!used.Add(id))
            {
                id = $"{id}_{f.Level}";
                used.Add(id);
            }
            result.Add((f, id));
        }
        return result;
    }

    private static XElement Feature(string type, string id, int level, string name, string description, string source, params XElement[] rules) =>
        new("element",
            new XAttribute("name", $"Level {level}: {name.Trim()}"),
            new XAttribute("type", type),
            new XAttribute("source", source),
            new XAttribute("id", id),
            new XElement("description", Paragraphs(description, name.Trim())),
            // <spellcasting> sits beside <rules>, as in the books
            rules.Where(r => r.Name == "spellcasting"),
            rules.Any(r => r.Name != "spellcasting") ? new XElement("rules", rules.Where(r => r.Name != "spellcasting")) : null);

    private static HomebrewFeatureModel FeatureFromXml(XElement e, int level)
    {
        var name = (string?)e.Attribute("name") ?? string.Empty;
        var colon = name.IndexOf(": ", StringComparison.Ordinal);
        return new HomebrewFeatureModel
        {
            Id = (string?)e.Attribute("id"),
            Level = level,
            Name = colon >= 0 && name.StartsWith("Level ", StringComparison.Ordinal) ? name[(colon + 2)..] : name,
            Description = Text(e.Element("description")),
        };
    }

    /// <summary>The elements of one record: the main element and every element whose id starts with its id + "_F_".</summary>
    public static IEnumerable<XElement> RecordElements(XElement root, string id) =>
        root.Elements("element").Where(e => (string?)e.Attribute("id") is { } x && (x == id || x.StartsWith(id + "_F_", StringComparison.Ordinal)));

    // ---- subclasses ----

    public static List<XElement> SubclassToXml(HomebrewSubclassModel m, string id)
    {
        var source = Source(m.Source);
        var features = FeatureIds(id, m.Features);
        var main = new XElement("element",
            new XAttribute("name", m.Name.Trim()),
            new XAttribute("type", "Archetype"),
            new XAttribute("source", source),
            new XAttribute("id", id),
            new XElement("supports", m.ClassSupports.Trim()),
            new XElement("description", Paragraphs(m.Description, m.Name.Trim())),
            new XElement("setters", new XElement("set", new XAttribute("name", "starlights-class"), m.ClassName.Trim())),
            new XElement("rules", features.Select(f => new XElement("grant",
                new XAttribute("type", "Archetype Feature"), new XAttribute("id", f.Id), new XAttribute("level", f.Feature.Level)))));
        return [main, .. features.Select(f => Feature("Archetype Feature", f.Id, f.Feature.Level, f.Feature.Name, f.Feature.Description, source))];
    }

    public static HomebrewSubclassModel SubclassFromXml(XElement root, XElement main)
    {
        var id = (string?)main.Attribute("id") ?? string.Empty;
        var levels = (main.Element("rules")?.Elements("grant") ?? []).ToDictionary(g => (string?)g.Attribute("id") ?? string.Empty, g => (int?)g.Attribute("level") ?? 1);
        return new HomebrewSubclassModel
        {
            Id = id,
            Name = (string?)main.Attribute("name") ?? string.Empty,
            Source = (string?)main.Attribute("source"),
            ClassSupports = main.Element("supports")?.Value ?? string.Empty,
            ClassName = Set(main, "starlights-class")?.Value ?? string.Empty,
            Description = Text(main.Element("description")),
            Features = RecordElements(root, id).Where(e => e != main)
                .Select(e => FeatureFromXml(e, levels.GetValueOrDefault((string?)e.Attribute("id") ?? string.Empty, 1)))
                .OrderBy(f => f.Level).ToList(),
        };
    }

    /// <summary>
    /// The classes a subclass can be made for, read from the content: each Class, through the Class Features it grants,
    /// to its subclass choice (select type="Archetype"); the last of that choice's alternatives is what a subclass names.
    /// </summary>
    public static List<SubclassTarget> SubclassTargets(IEnumerable<(string Type, string AuroraId, string Name, string? Source, string RawXml)> elements)
    {
        var list = elements.ToList();
        var archetypeSupports = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var feature in list.Where(e => e.Type == "Class Feature"))
        {
            var select = Parse(feature.RawXml)?.Descendants("select").FirstOrDefault(s => (string?)s.Attribute("type") == "Archetype");
            if ((string?)select?.Attribute("supports") is { Length: > 0 } supports)
            {
                // the last alternative is the one only this class's choice has ("Martial Archetype||Fighter Subclass":
                // the 2024 Fighter's own; the 2014 Fighter's choice has only "Martial Archetype", which the 2024 one also takes)
                archetypeSupports[feature.AuroraId] = supports.Split("||")[^1].Split('|')[0].Trim();
            }
        }
        var targets = new List<SubclassTarget>();
        foreach (var cls in list.Where(e => e.Type == "Class"))
        {
            var grants = Parse(cls.RawXml)?.Descendants("grant").Where(g => (string?)g.Attribute("type") == "Class Feature").Select(g => (string?)g.Attribute("id")) ?? [];
            if (grants.Select(g => g is null ? null : archetypeSupports.GetValueOrDefault(g)).FirstOrDefault(s => s is not null) is { } supports)
            {
                targets.Add(new SubclassTarget(cls.Name, cls.Source, supports));
            }
        }
        return targets.DistinctBy(t => (t.ClassName, t.Source, t.Supports)).OrderBy(t => t.ClassName, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Source).ToList();
    }

    private static XElement? Parse(string xml)
    {
        try
        {
            return XElement.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    // ---- classes ----

    private static string ProficiencyId(string kind, string name) => kind switch
    {
        "save" => $"ID_PROFICIENCY_SAVINGTHROW_{name.ToUpperInvariant()}",
        "armor" => name == "Shields" ? "ID_PROFICIENCY_ARMOR_PROFICIENCY_SHIELDS" : $"ID_PROFICIENCY_ARMOR_PROFICIENCY_{name.ToUpperInvariant()}_ARMOR",
        "weapon" => $"ID_PROFICIENCY_WEAPON_PROFICIENCY_{name.ToUpperInvariant()}_WEAPONS",
        _ => $"ID_PROFICIENCY_SKILL_{name.ToUpperInvariant().Replace(" ", string.Empty)}",
    };

    // the 2024 Cleric's (full), Paladin's (half) and Warlock's (pact) spellcasting tables: (level, amount gained)
    private static readonly Dictionary<int, (int Level, int Value)[]> FullSlots = new()
    {
        [1] = [(1, 2), (2, 1), (3, 1)], [2] = [(3, 2), (4, 1)], [3] = [(5, 2), (6, 1)], [4] = [(7, 1), (8, 1), (9, 1)],
        [5] = [(9, 1), (10, 1), (18, 1)], [6] = [(11, 1), (19, 1)], [7] = [(13, 1), (20, 1)], [8] = [(15, 1)], [9] = [(17, 1)],
    };
    private static readonly (int Level, int Value)[] FullPrepare =
        [(1, 4), (2, 1), (3, 1), (4, 1), (5, 2), (6, 1), (7, 1), (8, 1), (9, 2), (10, 1), (11, 1), (13, 1), (15, 1), (17, 1), (18, 1), (19, 1), (20, 1)];
    private static readonly Dictionary<int, (int Level, int Value)[]> HalfSlots = new()
    {
        [1] = [(1, 2), (3, 1), (5, 1)], [2] = [(5, 2), (7, 1)], [3] = [(9, 2), (11, 1)], [4] = [(13, 1), (15, 1), (17, 1)], [5] = [(17, 1), (19, 1)],
    };
    private static readonly (int Level, int Number)[] HalfSpells = [(1, 2), (2, 1), (3, 1), (4, 1), (5, 1), (7, 1), (9, 2), (11, 1), (13, 1), (15, 1), (17, 2), (19, 1)];
    private static readonly (int Level, int Value)[] PactSlotCount = [(1, 1), (2, 1), (11, 1), (17, 1)];
    private static readonly (int Level, int Value)[] PactSlotLevel = [(1, 1), (3, 1), (5, 1), (7, 1), (9, 1)];
    private static readonly (int Level, int Number)[] PactSpells = [(1, 2), (2, 1), (3, 1), (4, 1), (5, 1), (6, 1), (7, 1), (8, 1), (9, 1), (11, 1), (13, 1), (15, 1), (17, 1), (19, 1)];

    private static XElement Stat(string name, int value, int level) =>
        new("stat", new XAttribute("name", name), new XAttribute("value", value), level > 1 ? new XAttribute("level", level) : null);

    private static XElement SpellSelect(string name, string supports, int level, int number, string spellcasting) =>
        new("select", new XAttribute("type", "Spell"), new XAttribute("name", name), new XAttribute("supports", supports), new XAttribute("level", level),
            number > 1 ? new XAttribute("number", number) : null, new XAttribute("spellcasting", spellcasting));

    /// <summary>The name a class's statistics use ("Blood Hunter" → "blood-hunter"), as the spell options read them.</summary>
    public static string StatName(string className) => className.Trim().ToLowerInvariant().Replace(' ', '-');

    /// <summary>What the class's subclass choice asks for: the subclasses whose "supports" is this ("Test Warden Subclass" by default).</summary>
    public static string SubclassKey(HomebrewClassModel m) => string.IsNullOrWhiteSpace(m.SubclassSupports) ? $"{m.Name.Trim()} Subclass" : m.SubclassSupports.Trim();

    public static List<XElement> ClassToXml(HomebrewClassModel m, string id)
    {
        var source = Source(m.Source);
        var name = m.Name.Trim();
        var multiclassId = id.Replace("_CLASS_", "_MULTICLASS_", StringComparison.Ordinal);
        var notMulticlass = new XAttribute("requirements", $"!{multiclassId}");
        var subclassSupports = SubclassKey(m);
        var listName = string.IsNullOrWhiteSpace(m.SpellListName) ? name : m.SpellListName.Trim();

        var rules = new XElement("rules");
        foreach (var armor in ArmorKinds.Where(a => m.Armor.Contains(a)))
        {
            rules.Add(new XElement("grant", new XAttribute("type", "Proficiency"), new XAttribute("id", ProficiencyId("armor", armor)),
                m.MulticlassArmor.Contains(armor) ? null : new XAttribute(notMulticlass)));
        }
        foreach (var weapon in WeaponKinds.Where(w => m.Weapons.Contains(w)))
        {
            rules.Add(new XElement("grant", new XAttribute("type", "Proficiency"), new XAttribute("id", ProficiencyId("weapon", weapon)),
                m.MulticlassWeapons.Contains(weapon) ? null : new XAttribute(notMulticlass)));
        }
        foreach (var save in Abilities.Where(a => m.SavingThrows.Contains(a)))
        {
            rules.Add(new XElement("grant", new XAttribute("type", "Proficiency"), new XAttribute("id", ProficiencyId("save", save)), new XAttribute(notMulticlass)));
        }
        var skills = Skills.Where(s => m.Skills.Contains(s)).Select(s => ProficiencyId("skill", s)).ToList();
        if (m.SkillChoices > 0 && skills.Count > 0)
        {
            var supports = string.Join("|", skills);
            rules.Add(new XElement("select", new XAttribute("type", "Proficiency"), new XAttribute("name", $"Skill Proficiency ({name})"),
                new XAttribute("supports", supports), m.SkillChoices > 1 ? new XAttribute("number", m.SkillChoices) : null, new XAttribute(notMulticlass)));
            rules.Add(new XElement("select", new XAttribute("type", "Proficiency"), new XAttribute("name", $"Skill Proficiency ({name}, multiclass)"),
                new XAttribute("supports", supports), new XAttribute("requirements", multiclassId)));
        }

        var features = new List<XElement>();
        void Add(string featureId, int level, string featureName, string description, params XElement[] featureRules)
        {
            rules.Add(new XElement("grant", new XAttribute("type", "Class Feature"), new XAttribute("id", featureId), level > 1 ? new XAttribute("level", level) : null));
            features.Add(Feature("Class Feature", featureId, level, featureName, description, source, featureRules));
        }

        if (m.Casting is "Full" or "Half" or "Pact")
        {
            Add($"{id}_F_SPELLCASTING", 1, "Spellcasting", SpellcastingText(m, listName), SpellcastingRules(m, name, listName));
        }
        foreach (var (feature, featureId) in FeatureIds(id, m.Features.Where(f => !f.Name.Equals("Spellcasting", StringComparison.OrdinalIgnoreCase))))
        {
            Add(featureId, feature.Level, feature.Name, feature.Description);
        }
        Add($"{id}_F_SUBCLASS", m.SubclassLevel, $"{name} Subclass", $"You choose a {name} subclass.",
            new XElement("select", new XAttribute("type", "Archetype"), new XAttribute("name", $"{name} Subclass"), new XAttribute("supports", subclassSupports), new XAttribute("level", m.SubclassLevel)));
        var asi = m.AsiLevels.Where(l => l is >= 1 and <= 20).Distinct().Order().ToList();
        if (asi.Count > 0)
        {
            Add($"{id}_F_ABILITY_SCORE_IMPROVEMENT", asi[0], "Ability Score Improvement",
                $"You gain the Ability Score Improvement feat or another feat of your choice for which you qualify. You gain this feature again at {name} levels {string.Join(", ", asi.Skip(1))}.",
                [.. asi.Select(l => new XElement("select", new XAttribute("type", "Feat"), new XAttribute("name", $"Ability Score Improvement ({name} {l})"), new XAttribute("level", l)))]);
        }
        if (m.EpicBoon)
        {
            Add($"{id}_F_EPIC_BOON", 19, "Epic Boon", "You gain an Epic Boon feat or another feat of your choice for which you qualify.",
                new XElement("select", new XAttribute("type", "Feat"), new XAttribute("name", $"Epic Boon ({name} 19)"), new XAttribute("level", 19)));
        }

        var proficiencies = string.Join(", ", m.MulticlassWeapons.Select(w => $"{w} weapons").Concat(m.MulticlassArmor.Select(a => a == "Shields" ? "Shields" : $"{a} armor"))
            .Concat(m.SkillChoices > 0 && skills.Count > 0 ? ["one skill from its list"] : []));
        var ability = Abilities.FirstOrDefault(a => a.Equals(m.MulticlassAbility, StringComparison.OrdinalIgnoreCase));
        var multiclass = new XElement("multiclass", new XAttribute("id", multiclassId),
            ability is null ? null : new XElement("prerequisite", $"{ability} 13"),
            new XElement("requirements", ability is null ? $"!{id}||ID_INTERNAL_GRANTS_MULTICLASS_UNLOCKER" : $"([{ability[..3].ToLowerInvariant()}:13],!{id})||ID_INTERNAL_GRANTS_MULTICLASS_UNLOCKER"),
            new XElement("setters", new XElement("set", new XAttribute("name", "multiclass proficiencies"), proficiencies.Length > 0 ? proficiencies : "—")),
            new XElement("rules", new XElement("grant", new XAttribute("type", "Grants"), new XAttribute("id", "ID_INTERNAL_GRANT_MULTICLASS"))));

        var main = new XElement("element",
            new XAttribute("name", name),
            new XAttribute("type", "Class"),
            new XAttribute("source", source),
            new XAttribute("id", id),
            new XElement("description", Paragraphs(m.Description, name)),
            new XElement("setters",
                new XElement("set", new XAttribute("name", "hd"), $"d{m.HitDie}"),
                new XElement("set", new XAttribute("name", "starlights-subclass-supports"), subclassSupports),
                new XElement("set", new XAttribute("name", "starlights-casting"), m.Casting),
                new XElement("set", new XAttribute("name", "starlights-spell-list"), listName)),
            rules,
            multiclass);
        return [main, .. features];
    }

    private static string SpellcastingText(HomebrewClassModel m, string listName)
    {
        var lists = string.Join(", ", new[] { listName }.Concat(m.SpellLists).Distinct(StringComparer.OrdinalIgnoreCase));
        var how = m.Casting switch
        {
            "Full" => "You prepare spells from your spell list, as a 2024 Cleric does, and gain spell slots as a full spellcaster.",
            "Half" => "You learn spells from your spell list, as a 2024 Paladin does, and gain spell slots as a half spellcaster.",
            _ => "You learn spells from your spell list, as a 2024 Warlock does, and cast them with Pact Magic slots that come back on a Short or Long Rest.",
        };
        return $"{how}\n\nSpell lists: {lists}. Spellcasting ability: {m.CastingAbility ?? "Intelligence"}.";
    }

    private static XElement[] SpellcastingRules(HomebrewClassModel m, string className, string listName)
    {
        var stat = StatName(className);
        var lists = string.Join(", ", new[] { listName }.Concat(m.SpellLists).Distinct(StringComparer.OrdinalIgnoreCase));
        var ability = Abilities.FirstOrDefault(a => a.Equals(m.CastingAbility, StringComparison.OrdinalIgnoreCase)) ?? "Intelligence";
        var result = new List<XElement>
        {
            new XElement("grant", new XAttribute("type", "Grants"), new XAttribute("id", "ID_INTERNAL_GRANTS_SPELLCASTING_FEATURE")),
            new XElement("grant", new XAttribute("type", "Grants"), new XAttribute("id", m.Casting switch
            {
                "Full" => "ID_INTERNAL_GRANT_MULTICLASS_SPELLCASTING_SLOTS_FULL",
                "Half" => "ID_INTERNAL_GRANT_MULTICLASS_SPELLCASTING_SLOTS_HALF",
                _ => "ID_INTERNAL_GRANT_MULTICLASS_SPELLCASTING_SLOTS_SOLO",
            }), new XAttribute("requirements", "ID_INTERNAL_GRANT_MULTICLASS")),
        };
        var cantrips = Math.Clamp(m.Cantrips, 0, 6);
        if (m.Casting == "Full")
        {
            result.AddRange(FullSlots.SelectMany(s => s.Value.Select(v => Stat($"{stat}:spellcasting:slots:{s.Key}", v.Value, v.Level))));
            result.AddRange(FullPrepare.Select(p => Stat($"{stat}:spellcasting:prepare", p.Value, p.Level)));
        }
        else if (m.Casting == "Half")
        {
            result.AddRange(HalfSlots.SelectMany(s => s.Value.Select(v => Stat($"{stat}:spellcasting:slots:{s.Key}", v.Value, v.Level))));
            result.AddRange(HalfSpells.Select(s => SpellSelect($"Spell ({className})", "$(spellcasting:list),$(spellcasting:slots)", s.Level, s.Number, className)));
            cantrips = 0;
        }
        else
        {
            result.AddRange(PactSlotCount.Select(s => Stat($"{stat}:spellcasting:slots:count", s.Value, s.Level)));
            result.AddRange(PactSlotLevel.Select(s => Stat($"{stat}:spellcasting:slot", s.Value, s.Level)));
            result.AddRange(PactSpells.Select(s => SpellSelect($"Spell ({className})", "$(spellcasting:list),$(spellcasting:slots)", s.Level, s.Number, className)));
        }
        if (cantrips > 0)
        {
            result.Add(SpellSelect($"Cantrip ({className})", "$(spellcasting:list), 0", 1, cantrips, className));
            result.Add(SpellSelect($"Cantrip ({className})", "$(spellcasting:list), 0", 4, 1, className));
            result.Add(SpellSelect($"Cantrip ({className})", "$(spellcasting:list), 0", 10, 1, className));
        }
        var spellcasting = new XElement("spellcasting", new XAttribute("name", className), new XAttribute("ability", ability),
            m.Casting == "Full" ? new XAttribute("prepare", "true") : new XAttribute("allowReplace", "true"),
            new XElement("list", m.Casting == "Full" ? new XAttribute("known", "true") : null, lists));
        return [spellcasting, .. result];
    }

    public static HomebrewClassModel ClassFromXml(XElement root, XElement main)
    {
        var id = (string?)main.Attribute("id") ?? string.Empty;
        var rules = main.Element("rules")?.Elements().ToList() ?? [];
        string? Grant(string proficiencyId) => rules.FirstOrDefault(r => r.Name == "grant" && (string?)r.Attribute("id") == proficiencyId) is { } g
            ? ((string?)g.Attribute("requirements") is null ? "multi" : "single") : null;
        var features = RecordElements(root, id).Where(e => e != main).ToDictionary(e => (string?)e.Attribute("id") ?? string.Empty);
        var levels = rules.Where(r => r.Name == "grant" && (string?)r.Attribute("type") == "Class Feature")
            .ToDictionary(g => (string?)g.Attribute("id") ?? string.Empty, g => (int?)g.Attribute("level") ?? 1);
        var skillSelect = rules.FirstOrDefault(r => r.Name == "select" && (string?)r.Attribute("type") == "Proficiency" && (string?)r.Attribute("requirements") is { } req && req.StartsWith('!'));
        var skillIds = ((string?)skillSelect?.Attribute("supports") ?? string.Empty).Split('|');
        var spellcasting = features.GetValueOrDefault($"{id}_F_SPELLCASTING");
        var casting = spellcasting?.Element("spellcasting");
        var listName = Set(main, "starlights-spell-list")?.Value ?? (string?)main.Attribute("name") ?? string.Empty;
        var lists = (casting?.Element("list")?.Value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var asi = features.GetValueOrDefault($"{id}_F_ABILITY_SCORE_IMPROVEMENT")?.Element("rules")?.Elements("select").Select(s => (int?)s.Attribute("level") ?? 0).ToList() ?? [];
        var multiclassRequirements = main.Element("multiclass")?.Element("requirements")?.Value ?? string.Empty;
        var hd = Set(main, "hd")?.Value ?? "d8";
        string[] reserved = [$"{id}_F_SPELLCASTING", $"{id}_F_SUBCLASS", $"{id}_F_ABILITY_SCORE_IMPROVEMENT", $"{id}_F_EPIC_BOON"];
        return new HomebrewClassModel
        {
            Id = id,
            Name = (string?)main.Attribute("name") ?? string.Empty,
            Source = (string?)main.Attribute("source"),
            Description = Text(main.Element("description")),
            HitDie = int.TryParse(hd.TrimStart('d'), out var die) ? die : 8,
            SavingThrows = Abilities.Where(a => Grant(ProficiencyId("save", a)) is not null).ToList(),
            Armor = ArmorKinds.Where(a => Grant(ProficiencyId("armor", a)) is not null).ToList(),
            Weapons = WeaponKinds.Where(w => Grant(ProficiencyId("weapon", w)) is not null).ToList(),
            MulticlassArmor = ArmorKinds.Where(a => Grant(ProficiencyId("armor", a)) == "multi").ToList(),
            MulticlassWeapons = WeaponKinds.Where(w => Grant(ProficiencyId("weapon", w)) == "multi").ToList(),
            SkillChoices = skillSelect is null ? 0 : (int?)skillSelect.Attribute("number") ?? 1,
            Skills = Skills.Where(s => skillIds.Contains(ProficiencyId("skill", s))).ToList(),
            MulticlassAbility = Abilities.FirstOrDefault(a => multiclassRequirements.Contains($"[{a[..3].ToLowerInvariant()}:13]", StringComparison.Ordinal)),
            SubclassLevel = levels.GetValueOrDefault($"{id}_F_SUBCLASS", 3),
            SubclassSupports = Set(main, "starlights-subclass-supports")?.Value,
            AsiLevels = asi,
            EpicBoon = features.ContainsKey($"{id}_F_EPIC_BOON"),
            Features = features.Values.Where(e => !reserved.Contains((string?)e.Attribute("id")))
                .Select(e => FeatureFromXml(e, levels.GetValueOrDefault((string?)e.Attribute("id") ?? string.Empty, 1))).OrderBy(f => f.Level).ToList(),
            Casting = Set(main, "starlights-casting")?.Value ?? "None",
            CastingAbility = (string?)casting?.Attribute("ability"),
            SpellLists = lists.Where(l => !l.Equals(listName, StringComparison.OrdinalIgnoreCase)).ToList(),
            Cantrips = (int?)spellcasting?.Element("rules")?.Elements("select").FirstOrDefault(s => ((string?)s.Attribute("name"))?.StartsWith("Cantrip", StringComparison.Ordinal) == true && (int?)s.Attribute("level") == 1)?.Attribute("number")
                ?? (spellcasting?.Element("rules")?.Elements("select").Any(s => ((string?)s.Attribute("name"))?.StartsWith("Cantrip", StringComparison.Ordinal) == true) == true ? 1 : 0),
            SpellListName = listName,
        };
    }
}
