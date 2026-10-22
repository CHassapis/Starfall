using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Starlights.Modules.Elements.Services.FiveETools;

/// <summary>Spells, feats, class options, backgrounds and species.</summary>
internal sealed partial class FiveEToolsContent
{
    private static string Name(JsonObject entry) => FiveEToolsJson.Str(entry, "name") ?? "";

    private static string SourceOf(JsonObject entry) => FiveEToolsJson.Str(entry, "source") ?? "";

    // a fixed order, so the generated file is the same on every run
    private static IEnumerable<JsonObject> Ordered(IEnumerable<JsonObject> entries) =>
        entries.OrderBy(SourceOf, StringComparer.Ordinal).ThenBy(Name, StringComparer.Ordinal);

    private void Spells()
    {
        var lookup = _json.LoadObject("spells/sources.json");
        foreach (var spell in Ordered(_json.Load("spells/spells-*.json", "spell")))
        {
            var (name, source) = (Name(spell), SourceOf(spell));
            if (Skip(spell, "spell") || name.Length == 0 || AuroraHas("spell", spell))
            {
                continue;
            }
            var modern = IsModern(spell);
            var element = NewElement("spells", name, "Spell", source, IdOf("SPELL", source, name), modern, Description(spell["entries"], spell["entriesHigherLevel"]));

            // the class lists it is on: what Aurora's spell lists ($(spellcasting:list)) match
            var entry = lookup?[source]?[name];
            var classes = new[] { "class", "classVariant" }
                .SelectMany(k => entry?[k] is JsonArray list ? list.OfType<JsonObject>().Select(c => FiveEToolsJson.Str(c, "name")) : [])
                .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
            if (classes.Count > 0)
            {
                element.Add(new XElement("supports", string.Join(", ", classes)));
            }

            var setters = new XElement("setters");
            void Set(string key, string? value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    setters.Add(new XElement("set", new XAttribute("name", key), value));
                }
            }
            var ritual = FiveEToolsJson.Bool(spell["meta"], "ritual");
            var time = FiveEToolsText.CastingTime(spell["time"]);
            Set("level", (FiveEToolsJson.Int(spell, "level") ?? 0).ToString());
            Set("school", FiveEToolsText.School(FiveEToolsJson.Str(spell, "school")));
            Set("time", ritual ? $"{time} or Ritual" : time);
            if (ritual)
            {
                Set("isRitual", "true");
            }
            Set("range", FiveEToolsText.Range(spell["range"]));
            var components = spell["components"];
            if (FiveEToolsJson.Bool(components, "v"))
            {
                Set("hasVerbalComponent", "true");
            }
            if (FiveEToolsJson.Bool(components, "s"))
            {
                Set("hasSomaticComponent", "true");
            }
            if (components?["m"] is { } material)
            {
                Set("hasMaterialComponent", "true");
                Set("materialComponent", material is JsonObject m ? FiveEToolsJson.Str(m, "text") : material.ToString());
            }
            var (duration, concentration) = FiveEToolsText.Duration(spell["duration"]);
            if (concentration)
            {
                Set("isConcentration", "true");
            }
            Set("duration", duration);
            element.Add(setters);
        }
    }

    /// <summary>
    /// Aurora feats whose prerequisite is only words (Eldritch Adept, Fighting Initiate) or missing (the 2014 War Caster)
    /// get the requirements 5etools records for the same feat of the same book, so the builder can tell who qualifies;
    /// with requirements of Aurora's own (a feat's "not with its other edition"), both have to hold. Returns how many.
    /// </summary>
    public int FillFeatRequirements(IEnumerable<XElement> feats)
    {
        var byName = _json.Load("feats.json", "feat").GroupBy(f => FiveEToolsAuroraIndex.Words(Name(f))).ToDictionary(g => g.Key, g => g.ToList());
        var filled = 0;
        foreach (var feat in feats)
        {
            if (!byName.TryGetValue(FiveEToolsAuroraIndex.Words((string?)feat.Attribute("name") ?? ""), out var candidates))
            {
                continue;
            }
            var book = (string?)feat.Attribute("source");
            var modern = FiveEToolsAuroraIndex.IsModern(new AuroraCatalogElement((string?)feat.Attribute("id") ?? "", "", "Feat", book, [], feat));
            var match = candidates.FirstOrDefault(c => string.Equals(SourceName(SourceOf(c)), book, StringComparison.OrdinalIgnoreCase))
                ?? (candidates.Count == 1 ? candidates[0] : candidates.FirstOrDefault(c => IsModern(c) == modern));
            if (match?["prerequisite"] is null)
            {
                continue;
            }
            var matchModern = IsModern(match);
            var (text, requirements) = Prerequisite(match["prerequisite"], matchModern, matchModern ? "ID_WOTC_PHB24_GRANTS_FEAT_UNLOCKER" : "ID_WOTC_PHB_GRANTS_FEAT_UNLOCKER");
            if (requirements is null)
            {
                continue;
            }
            if (feat.Element("requirements") is { } own)
            {
                own.Value = $"({own.Value.Trim()}),({requirements})";
            }
            else
            {
                feat.Add(new XElement("requirements", requirements));
            }
            if (feat.Element("prerequisite") is null && feat.Element("prerequisites") is null && text is not null)
            {
                feat.Add(new XElement("prerequisite", text));
            }
            filled++;
        }
        return filled;
    }

    // 5etools feat categories as the supports Aurora's choices ask for (Origin feats for a background, …)
    private static readonly Dictionary<string, string> FeatCategories = new()
    {
        ["O"] = "Origin",
        ["G"] = "General",
        ["FS"] = "Fighting Style",
        ["EB"] = "Epic Boon",
    };

    private void Feats()
    {
        foreach (var feat in Ordered(_json.Load("feats.json", "feat")))
        {
            var (name, source) = (Name(feat), SourceOf(feat));
            if (Skip(feat, "feat") || name.Length == 0 || AuroraHas("feat", feat))
            {
                continue;
            }
            var modern = IsModern(feat);
            var element = NewElement("feats", name, "Feat", source, IdOf("FEAT", source, name), modern, Description(feat["entries"]));
            if (FiveEToolsJson.Str(feat, "category") is { } category && FeatCategories.TryGetValue(category, out var support))
            {
                element.Add(new XElement("supports", support));
            }
            var (text, requirements) = Prerequisite(feat["prerequisite"], modern, modern ? "ID_WOTC_PHB24_GRANTS_FEAT_UNLOCKER" : "ID_WOTC_PHB_GRANTS_FEAT_UNLOCKER");
            if (text is not null)
            {
                element.Add(new XElement("prerequisite", text));
            }
            if (requirements is not null)
            {
                element.Add(new XElement("requirements", requirements));
            }
            if (FiveEToolsJson.Bool(feat, "repeatable"))
            {
                element.Add(new XElement("setters", new XElement("set", new XAttribute("name", "allow duplicate"), "true")));
            }
            element.Add(Sheet(element.Element("description")!));

            var rules = RulesOf(element);
            AbilityScores(rules, feat["ability"], name);
            Proficiencies(rules, feat, name, modern);
            AdditionalSpells(rules, feat["additionalSpells"], name, modern);
            Tidy(element);
        }
    }

    /// <summary>
    /// Class options (invocations, metamagic, maneuvers, fighting styles, …). The supports Aurora's choices ask for are
    /// learned from the options Aurora already has: the new Eldritch Invocation gets what Aurora's invocations of the same
    /// edition carry. A kind Aurora has none of has no choice to offer it in, so it is left out.
    /// </summary>
    private void OptionalFeatures()
    {
        var options = Ordered(_json.Load("optionalfeatures.json", "optionalfeature")).Where(o => !Skip(o, "optionalfeature")).ToList();
        string[] types = ["Class Feature", "Archetype Feature", "Feat", "Feat Feature"];

        var learned = _optionSupports;
        foreach (var option in options)
        {
            var modern = IsModern(option);
            var match = types.SelectMany(t => _aurora.Find(t, Name(option), SourceName(SourceOf(option)), modern)).FirstOrDefault();
            if (match is null)
            {
                continue;
            }
            foreach (var kind in Kinds(option))
            {
                var counts = learned.TryGetValue((kind, modern), out var c) ? c : learned[(kind, modern)] = new(StringComparer.Ordinal);
                foreach (var support in match.Supports.Where(s => !s.StartsWith("ID_", StringComparison.Ordinal)))
                {
                    counts[support] = counts.GetValueOrDefault(support) + 1;
                }
            }
        }
        foreach (var option in options)
        {
            var (name, source) = (Name(option), SourceOf(option));
            if (name.Length == 0 || AuroraHas("optionalfeature", option))
            {
                continue;
            }
            var modern = IsModern(option);
            var supports = Kinds(option).Select(k => OptionSupport(k, modern)).OfType<string>().Distinct().ToList();
            if (supports.Count == 0)
            {
                continue;
            }
            var element = NewElement("class options", name, "Class Feature", source, IdOf("OPTION", source, name), modern, Description(option["entries"]));
            element.Add(new XElement("supports", string.Join(", ", supports)));
            var (text, requirements) = Prerequisite(option["prerequisite"], modern);
            if (text is not null)
            {
                element.Add(new XElement("prerequisite", text));
            }
            if (requirements is not null)
            {
                element.Add(new XElement("requirements", requirements));
            }
            element.Add(Sheet(element.Element("description")!, name));
            var rules = RulesOf(element);
            AdditionalSpells(rules, option["additionalSpells"], name, modern);
            Tidy(element);
        }

        static IEnumerable<string> Kinds(JsonObject option) =>
            option["featureType"] is JsonArray kinds ? kinds.Select(k => k?.ToString()).OfType<string>() : [];
    }

    // what Aurora's class options of each 5etools kind ("EI", "MM", …) and edition carry in their supports, counted
    private readonly Dictionary<(string Kind, bool Modern), Dictionary<string, int>> _optionSupports = [];

    /// <summary>The supports a class option of this kind gets: what most of Aurora's own of that kind carry.</summary>
    private string? OptionSupport(string kind, bool modern) =>
        (_optionSupports.GetValueOrDefault((kind, modern)) ?? _optionSupports.GetValueOrDefault((kind, !modern)))?
            .OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key).FirstOrDefault();

    private void Backgrounds()
    {
        foreach (var background in Ordered(_json.Load("backgrounds.json", "background")))
        {
            var (name, source) = (Name(background), SourceOf(background));
            if (Skip(background, "background") || name.Length == 0 || AuroraHas("background", background))
            {
                continue;
            }
            var modern = IsModern(background) || background["ability"] is not null;
            var id = IdOf("BACKGROUND", source, name);
            var element = NewElement("backgrounds", name, "Background", source, id, modern, Description(background["entries"]));
            var rules = RulesOf(element);

            // 2024: +2/+1 or +1/+1/+1 among three abilities, Aurora's combination of those three
            if (background["ability"] is JsonArray ability && ability.FirstOrDefault()?["choose"]?["weighted"]?["from"] is JsonArray from)
            {
                var order = FiveEToolsText.Abilities.ToList();
                var three = from.Select(a => a?.ToString() ?? "").OrderBy(order.IndexOf).Select(a => a.ToUpperInvariant()).ToList();
                var combination = $"ID_INTERNAL_ABILITY_SCORE_IMPROVEMENT_COMBINATION_{string.Join('_', three)}";
                if (_aurora.Exists(combination))
                {
                    rules.Add(Grant("Ability Score Improvement", combination));
                    if (_aurora.Exists("ID_INTERNAL_GRANTS_BACKGROUND_ASI"))
                    {
                        rules.Add(Grant("Grants", "ID_INTERNAL_GRANTS_BACKGROUND_ASI"));
                    }
                }
            }
            else
            {
                AbilityScores(rules, background["ability"], name);
            }

            Feats(rules, background["feats"], name, modern);
            Proficiencies(rules, background, name, modern);
            AdditionalSpells(rules, background["additionalSpells"], name, modern);

            // 2014: the background's feature ("Feature: Shelter of the Faithful") as an element of its own
            if (!modern && background["entries"] is JsonArray entries)
            {
                foreach (var feature in entries.OfType<JsonObject>().Where(e => FiveEToolsJson.Str(e, "name")?.StartsWith("Feature:", StringComparison.Ordinal) == true))
                {
                    var featureName = FiveEToolsJson.Str(feature, "name")!["Feature:".Length..].Trim();
                    // the same feature Aurora has (a background reprinted with its feature) is granted, not copied
                    if (Find("Background Feature", featureName) is { } existing)
                    {
                        rules.Add(Grant("Background Feature", existing));
                        continue;
                    }
                    var featureElement = NewElement("background features", featureName, "Background Feature", source, IdOf("BACKGROUND_FEATURE", source, name, featureName), modern, Description(feature["entries"]));
                    featureElement.Add(Sheet(featureElement.Element("description")!, featureName));
                    rules.Add(Grant("Background Feature", (string)featureElement.Attribute("id")!));
                }
            }
            Tidy(element);
        }
    }

    /// <summary>Feats given outright ("magic initiate; cleric|xphb") or chosen ("any Origin feat").</summary>
    private void Feats(XElement rules, JsonNode? node, string owner, bool modern)
    {
        if (node is not JsonArray alternatives || alternatives.FirstOrDefault() is not JsonObject set)
        {
            return;
        }
        foreach (var (key, value) in set)
        {
            if (key == "any" && value is JsonValue any && any.TryGetValue<int>(out var count))
            {
                rules.Add(Select("Feat", $"Feat ({owner})", null, count));
            }
            else if (key == "anyFromCategory" && value is JsonObject category)
            {
                var categories = category["category"] is JsonArray c ? c.Select(x => FeatCategories.GetValueOrDefault(x?.ToString() ?? "")).OfType<string>().ToList() : [];
                rules.Add(Select("Feat", $"Feat ({owner})", string.Join("||", categories), FiveEToolsJson.Int(category, "count") ?? 1));
            }
            else if (value is JsonValue granted && granted.TryGetValue<bool>(out var yes) && yes)
            {
                var reference = key.Split('|');
                if (Find("Feat", FeatName(reference[0]), reference.ElementAtOrDefault(1)?.ToUpperInvariant(), modern) is { } id)
                {
                    rules.Add(Grant("Feat", id));
                }
            }
        }
    }

    // traits that are part of every species' description, not features of their own
    private static readonly HashSet<string> NotTraits = new(StringComparer.OrdinalIgnoreCase) { "Age", "Alignment", "Size", "Speed", "Languages", "Language", "Creature Type" };

    private void Species()
    {
        var subraces = _json.Load("races.json", "subrace");
        foreach (var race in Ordered(_json.Load("races.json", "race")))
        {
            var (name, source) = (Name(race), SourceOf(race));
            if (Skip(race, "race") || name.Length == 0 || AuroraHas("race", race))
            {
                continue;
            }
            var modern = IsModern(race);
            var element = NewElement("species", name, "Race", source, IdOf("RACE", source, name), modern, Description(race["entries"]));
            element.Add(new XElement("sheet", new XAttribute("display", "false")));
            var rules = RulesOf(element);
            SpeciesRules(rules, race, name, source, modern);

            // its subraces, when it has any (2014 species)
            var own = subraces.Where(s => FiveEToolsJson.Str(s, "raceName") == name && FiveEToolsJson.Str(s, "raceSource") == source && Name(s).Length > 0 && !Skip(s, "subrace")).ToList();
            if (own.Count > 0)
            {
                var token = $"{name} Subrace";
                rules.Add(Select("Sub Race", $"Subrace ({name})", token));
                foreach (var subrace in Ordered(own))
                {
                    Subrace(subrace, name, token);
                }
            }
        }

        // new subraces of the species Aurora has, offered by that species' own subrace choice
        foreach (var subrace in Ordered(subraces))
        {
            var (subName, raceName) = (Name(subrace), FiveEToolsJson.Str(subrace, "raceName") ?? "");
            if (subName.Length == 0 || Skip(subrace, "subrace") || !_aurora.Has("race", raceName))
            {
                continue;
            }
            if (_aurora.Has("race", $"{raceName} ({subName})", $"{subName} {raceName}", subName))
            {
                Leave("subrace", subrace, AuroraHasIt);
                continue;
            }
            var parent = _aurora.Find("Race", raceName, SourceName(FiveEToolsJson.Str(subrace, "raceSource") ?? "")).FirstOrDefault();
            var select = parent?.Xml.Element("rules")?.Elements("select").FirstOrDefault(s => (string?)s.Attribute("type") == "Sub Race");
            if (select is not null && SupportToken((string?)select.Attribute("supports")) is { } token)
            {
                Subrace(subrace, raceName, token);
            }
        }
    }

    private void Subrace(JsonObject subrace, string raceName, string token)
    {
        var (name, source) = (Name(subrace), SourceOf(subrace));
        var modern = IsModern(subrace);
        var element = NewElement("subraces", $"{raceName} ({name})", "Sub Race", source, IdOf("SUBRACE", source, raceName, name), modern, Description(subrace["entries"]));
        element.Add(new XElement("supports", token));
        element.Add(new XElement("sheet", new XAttribute("display", "false")));
        SpeciesRules(RulesOf(element), subrace, $"{raceName} ({name})", source, modern);
        Tidy(element);
    }

    /// <summary>A support that satisfies a choice's supports expression ("Dwarf Subrace", "(A||B),!C" → "A").</summary>
    private static string? SupportToken(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }
        var matches = SupportsExpression.Compile(expression);
        return expression.Split(['|', ',', '(', ')', '!'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !t.StartsWith("ID_", StringComparison.Ordinal) && !t.StartsWith("$(", StringComparison.Ordinal))
            .FirstOrDefault(t => matches([t], ""));
    }

    private void SpeciesRules(XElement rules, JsonObject race, string name, string source, bool modern)
    {
        // 2024 species: the ability scores come from the background, or the species with 2014 backgrounds (as Aurora does)
        if (modern && _aurora.Elements.Any(e => e.Supports.Contains("Custom Ability Score Improvement Option")))
        {
            rules.Add(Select("Ability Score Improvement", $"Ability Score Improvement Option ({name})", "Custom Ability Score Improvement Option",
                requirements: _aurora.Exists("ID_INTERNAL_GRANTS_BACKGROUND_ASI") ? "!ID_INTERNAL_GRANTS_BACKGROUND_ASI" : null));
        }
        else
        {
            AbilityScores(rules, race["ability"], name);
        }

        if (race["size"] is JsonArray sizes)
        {
            var codes = sizes.Select(s => s?.ToString()).ToList();
            var size = (codes.Contains("M") ? "M" : codes.FirstOrDefault()) switch
            {
                "T" => "ID_SIZE_TINY",
                "S" => "ID_SIZE_SMALL",
                "M" => "ID_SIZE_MEDIUM",
                "L" => "ID_SIZE_LARGE",
                _ => null,
            };
            if (size is not null && _aurora.Exists(size))
            {
                rules.Add(Grant("Size", size));
            }
        }

        switch (race["speed"])
        {
            case JsonValue walk when walk.TryGetValue<int>(out var feet):
                rules.Add(Stat("innate speed", feet.ToString(), bonus: "base"));
                break;
            case JsonObject speeds:
                foreach (var (kind, value) in speeds)
                {
                    var stat = kind == "walk" ? "innate speed" : $"innate speed:{kind}";
                    if (value is JsonValue v && v.TryGetValue<int>(out var amount))
                    {
                        rules.Add(Stat(stat, amount.ToString(), bonus: "base"));
                    }
                    else if (value is JsonValue same && same.TryGetValue<bool>(out var equal) && equal && kind != "walk")
                    {
                        rules.Add(Stat(stat, "innate speed", bonus: "base"));
                    }
                }
                break;
        }

        if (FiveEToolsJson.Int(race, "darkvision") is { } darkvision)
        {
            var vision = darkvision > 60 ? "ID_VISION_SUPERIORDARKVISION" : "ID_VISION_DARKVISION";
            if (_aurora.Exists(vision))
            {
                rules.Add(Grant("Vision", vision));
            }
        }

        Proficiencies(rules, race, name, modern);
        AdditionalSpells(rules, race["additionalSpells"], name, modern);
        Feats(rules, race["feats"], name, modern);

        // the traits, as elements of their own, so the sheet lists them
        if (race["entries"] is JsonArray entries)
        {
            foreach (var trait in entries.OfType<JsonObject>())
            {
                var traitName = FiveEToolsJson.Str(trait, "name");
                if (traitName is null || NotTraits.Contains(traitName))
                {
                    continue;
                }
                var traitElement = NewElement("species traits", traitName, "Racial Trait", source, IdOf("RACIAL_TRAIT", source, name, traitName), modern, Description(trait["entries"]));
                traitElement.Add(Sheet(traitElement.Element("description")!, traitName));
                rules.Add(Grant("Racial Trait", (string)traitElement.Attribute("id")!));
            }
        }
    }
}
