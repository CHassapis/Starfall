using System.Text.Json.Nodes;
using System.Xml.Linq;
using Starlights.Modules.Elements.Integration;

namespace Starlights.Modules.Elements.Services.FiveETools;

/// <summary>Classes and subclasses.</summary>
internal sealed partial class FiveEToolsContent
{
    // the classes generated here, by "name|source", for their subclasses
    private readonly Dictionary<string, (string Id, string SubclassSupport)> _classes = new(StringComparer.OrdinalIgnoreCase);

    private void Classes()
    {
        foreach (var cls in Ordered(_json.Load("class/class-*.json", "class")))
        {
            var (name, source) = (Name(cls), SourceOf(cls));
            if (Skip(cls, "class") || name.Length == 0 || AuroraHas("class", cls))
            {
                continue;
            }
            var modern = IsModern(cls);
            var id = IdOf("CLASS", source, name);
            var multiclassId = IdOf("MULTICLASS", source, name);
            var notMulticlass = $"!{multiclassId}";
            var subclassSupport = $"{name} Subclass";
            _classes[$"{name}|{source}"] = (id, subclassSupport);

            var element = NewElement("classes", name, "Class", source, id, modern, Description(ClassIntroduction(cls)));
            element.Add(new XElement("sheet", new XAttribute("display", "false")));
            if (cls["hd"] is JsonObject hd && FiveEToolsJson.Int(hd, "faces") is { } faces)
            {
                element.Add(new XElement("setters", new XElement("set", new XAttribute("name", "hd"), $"d{faces}")));
            }
            var rules = RulesOf(element);

            // what a character starting in this class gets (a multiclass character gets less, see below)
            if (cls["proficiency"] is JsonArray saves)
            {
                foreach (var save in saves.Select(s => _aurora.SavingThrow(s?.ToString() ?? "")).OfType<string>())
                {
                    rules.Add(Grant("Proficiency", save, requirements: notMulticlass));
                }
            }
            if (cls["startingProficiencies"] is JsonObject starting)
            {
                StartingProficiencies(rules, starting, name, modern, notMulticlass);
            }

            ClassFeatures(element, cls, name, source, modern, subclassSupport);
            element.Add(Multiclass(cls, multiclassId, name, modern));
        }
    }

    // the class's own text: its features are elements of their own, so only a line about it stays
    private JsonNode ClassIntroduction(JsonObject cls) =>
        new JsonArray(JsonValue.Create($"The {Name(cls)} class, from {BookName(SourceOf(cls))}. Its features are listed by level."));

    private void StartingProficiencies(XElement rules, JsonObject starting, string owner, bool modern, string? requirements)
    {
        var entry = new JsonObject();
        if (starting["skills"] is JsonArray skills)
        {
            entry["skillProficiencies"] = skills.DeepClone();
        }
        if (starting["toolProficiencies"] is JsonArray tools)
        {
            entry["toolProficiencies"] = tools.DeepClone();
        }
        Proficiencies(rules, entry, owner, modern, requirements);

        foreach (var armor in Items(starting["armor"]))
        {
            if (_aurora.Armor(armor) is { } id)
            {
                rules.Add(Grant("Proficiency", id, requirements: requirements));
            }
        }
        foreach (var weapon in Items(starting["weapons"]))
        {
            if (_aurora.Weapon(weapon) is { } id)
            {
                rules.Add(Grant("Proficiency", id, requirements: requirements));
            }
        }
        foreach (var tool in Items(starting["tools"]))
        {
            if ((_aurora.Tool(tool) ?? _aurora.Tool(Title(tool))) is { } id)
            {
                rules.Add(Grant("Proficiency", id, requirements: requirements));
            }
        }

        // "light", "{@item shield|phb|shields}" or {"proficiency": "…", "full": "…"}
        static IEnumerable<string> Items(JsonNode? node) => node is JsonArray list
            ? list.Select(i => i is JsonObject o ? FiveEToolsJson.Str(o, "proficiency") ?? "" : FiveEToolsRenderer.PlainText(i?.ToString() ?? "")).Where(i => i.Length > 0)
            : [];
    }

    /// <summary>The features by level: each its own element, with the subclass choice, ASIs, Epic Boon and spellcasting as rules.</summary>
    private void ClassFeatures(XElement element, JsonObject cls, string name, string source, bool modern, string subclassSupport)
    {
        var rules = RulesOf(element);
        var subclassTitle = FiveEToolsJson.Str(cls, "subclassTitle") ?? $"{name} Subclass";
        var subclassChosen = false;
        foreach (var item in cls["classFeatures"] is JsonArray features ? features : [])
        {
            var reference = item is JsonObject o ? FiveEToolsJson.Str(o, "classFeature") : item?.ToString();
            var gainsSubclass = item is JsonObject g && FiveEToolsJson.Bool(g, "gainSubclassFeature");
            if (reference is null)
            {
                continue;
            }
            if (ClassFeature(reference) is not { } feature)
            {
                Report(ImportDiagnosticCode.ClassFeatureMissing, $"{name} lists the feature {reference}, which is not in the 5etools data; it was left out.",
                    (string?)element.Attribute("id"), name, reference);
                continue;
            }
            var featureName = Name(feature);
            var level = FiveEToolsJson.Int(feature, "level") ?? 1;
            if (gainsSubclass && subclassChosen)
            {
                continue; // "Subclass Feature": the subclass grants its own
            }

            var featureId = IdOf("CLASS_FEATURE", source, name, featureName, level.ToString());
            var featureElement = NewElement("class features", $"Level {level}: {featureName}", "Class Feature", source, featureId, modern, Description(feature["entries"]));
            var featureRules = RulesOf(featureElement);
            if (gainsSubclass)
            {
                subclassChosen = true;
                featureElement.Add(new XElement("sheet", new XAttribute("display", "false")));
                featureRules.Add(Select("Archetype", subclassTitle, subclassSupport));
            }
            else if (featureName.Equals("Ability Score Improvement", StringComparison.OrdinalIgnoreCase))
            {
                featureElement.Add(new XElement("sheet", new XAttribute("display", "false")));
                // 2024: a feat (the Ability Score Improvement feat among them); 2014: Aurora's "Improvement Option" choice
                featureRules.Add(modern
                    ? Select("Feat", $"Ability Score Improvement ({name} {level})", null, level: level)
                    : Select("Class Feature", "Ability Score Improvement", $"Improvement Option,{name},{level}", level: level));
            }
            else if (featureName.Equals("Epic Boon", StringComparison.OrdinalIgnoreCase))
            {
                featureElement.Add(new XElement("sheet", new XAttribute("display", "false")));
                featureRules.Add(Select("Feat", $"Epic Boon ({name} {level})", _aurora.Elements.Any(e => e.Supports.Contains("Epic Boon")) ? "Epic Boon" : null, level: level));
            }
            else if (featureName is "Spellcasting" or "Pact Magic" && FiveEToolsJson.Str(cls, "spellcastingAbility") is { } ability)
            {
                featureElement.Add(Sheet(featureElement.Element("description")!, featureName));
                Spellcasting(featureElement, cls, name, ability, featureName == "Pact Magic" || FiveEToolsJson.Str(cls, "casterProgression") == "pact");
            }
            else
            {
                featureElement.Add(Sheet(featureElement.Element("description")!, featureName));
            }
            Tidy(featureElement);
            rules.Add(Grant("Class Feature", featureId, level));
        }

        // invocations, metamagic, maneuvers…: a choice of the class options Aurora (or this translation) has
        foreach (var progression in cls["optionalfeatureProgression"] is JsonArray p ? p.OfType<JsonObject>() : [])
        {
            var kinds = progression["featureType"] is JsonArray k ? k.Select(x => x?.ToString()).OfType<string>().ToList() : [];
            var supports = kinds.Select(kind => OptionSupport(kind, modern)).OfType<string>().Distinct().ToList();
            var label = FiveEToolsJson.Str(progression, "name") ?? "Option";
            var previous = 0;
            foreach (var (level, count) in Progression(progression["progression"]))
            {
                if (count > previous && supports.Count > 0)
                {
                    rules.Add(Select("Class Feature", $"{label} ({name})", string.Join("||", supports), count - previous, level));
                }
                previous = Math.Max(previous, count);
            }
        }
    }

    /// <summary>A per-level table: an array of 20 numbers, or {"level": count}.</summary>
    private static IEnumerable<(int Level, int Count)> Progression(JsonNode? node)
    {
        switch (node)
        {
            case JsonArray levels:
                for (var i = 0; i < levels.Count; i++)
                {
                    if (levels[i] is JsonValue v && v.TryGetValue<int>(out var count))
                    {
                        yield return (i + 1, count);
                    }
                }
                break;
            case JsonObject byLevel:
                var running = 0;
                foreach (var (key, value) in byLevel.OrderBy(p => int.TryParse(p.Key, out var l) ? l : 99))
                {
                    if (int.TryParse(key, out var level) && value is JsonValue v && v.TryGetValue<int>(out var count))
                    {
                        running = count;
                        yield return (level, running);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Spell slots, prepared or known spells and cantrips, from the class's tables, written the way Aurora's
    /// classes write them ("name:spellcasting:slots:1" per level), so the builder and the sheet read them alike.
    /// </summary>
    private void Spellcasting(XElement feature, JsonObject caster, string name, string ability, bool pact)
    {
        var slug = name.ToLowerInvariant().Replace(' ', '-');
        var prepares = caster["preparedSpellsProgression"] is not null || caster["preparedSpells"] is not null;
        feature.Add(new XElement("spellcasting",
            new XAttribute("name", name),
            new XAttribute("ability", FiveEToolsText.AbilityName(ability)),
            prepares ? new XAttribute("prepare", "true") : null,
            new XElement("list", name)));
        var rules = RulesOf(feature);

        var castingGrant = pact ? "ID_INTERNAL_GRANTS_PACT_MAGIC_FEATURE" : "ID_INTERNAL_GRANTS_SPELLCASTING_FEATURE";
        if (_aurora.Exists(castingGrant))
        {
            rules.Add(Grant("Grants", castingGrant));
        }
        var multiclassSlots = FiveEToolsJson.Str(caster, "casterProgression") switch
        {
            "full" => "ID_INTERNAL_GRANT_MULTICLASS_SPELLCASTING_SLOTS_FULL",
            "1/2" => "ID_INTERNAL_GRANT_MULTICLASS_SPELLCASTING_SLOTS_HALF",
            "1/3" => "ID_INTERNAL_GRANT_MULTICLASS_SPELLCASTING_SLOTS_THIRD",
            "artificer" => "ID_INTERNAL_GRANT_MULTICLASS_SPELLCASTING_SLOTS_HALF_UP",
            "pact" => "ID_INTERNAL_GRANT_MULTICLASS_SPELLCASTING_SLOTS_SOLO",
            _ => null,
        };
        if (multiclassSlots is not null && _aurora.Exists(multiclassSlots) && _aurora.Exists("ID_INTERNAL_GRANT_MULTICLASS"))
        {
            rules.Add(Grant("Grants", multiclassSlots, requirements: "ID_INTERNAL_GRANT_MULTICLASS"));
        }

        var groups = (caster["classTableGroups"] ?? caster["subclassTableGroups"]) is JsonArray g ? g.OfType<JsonObject>().ToList() : [];
        if (pact)
        {
            PactSlots(rules, groups, slug);
        }
        else if (groups.FirstOrDefault(t => t["rowsSpellProgression"] is JsonArray)?["rowsSpellProgression"] is JsonArray rows)
        {
            // slots gained at each level, per spell level
            var previous = new int[9];
            for (var level = 1; level <= rows.Count; level++)
            {
                var row = rows[level - 1] as JsonArray ?? [];
                for (var spellLevel = 1; spellLevel <= Math.Min(9, row.Count); spellLevel++)
                {
                    var count = row[spellLevel - 1]?.GetValue<int>() ?? 0;
                    if (count > previous[spellLevel - 1])
                    {
                        rules.Add(Stat($"{slug}:spellcasting:slots:{spellLevel}", (count - previous[spellLevel - 1]).ToString(), level));
                    }
                    previous[spellLevel - 1] = Math.Max(previous[spellLevel - 1], count);
                }
            }
        }

        Increments(caster["preparedSpellsProgression"], (level, more) => rules.Add(Stat($"{slug}:spellcasting:prepare", more.ToString(), level)));
        Increments(caster["cantripProgression"], (level, more) =>
            rules.Add(Select("Spell", $"Cantrip ({name})", "$(spellcasting:list), 0", more, level).WithSpellcasting(name)));
        if (!prepares)
        {
            Increments(caster["spellsKnownProgression"], (level, more) =>
                rules.Add(Select("Spell", $"Spell ({name})", "$(spellcasting:list), $(spellcasting:slots)", more, level).WithSpellcasting(name)));
        }

        static void Increments(JsonNode? progression, Action<int, int> add)
        {
            var previous = 0;
            foreach (var (level, count) in Progression(progression))
            {
                if (count > previous)
                {
                    add(level, count - previous);
                }
                previous = Math.Max(previous, count);
            }
        }
    }

    /// <summary>Pact Magic: a few slots, all of one level, from the "Spell Slots" and "Slot Level" columns, as Aurora's Warlock does.</summary>
    private static void PactSlots(XElement rules, List<JsonObject> groups, string slug)
    {
        foreach (var group in groups)
        {
            var labels = group["colLabels"] is JsonArray l ? l.Select(x => FiveEToolsRenderer.PlainText(x?.ToString() ?? "")).ToList() : [];
            var (slotsColumn, levelColumn) = (labels.FindIndex(x => x.Contains("Spell Slots", StringComparison.OrdinalIgnoreCase)), labels.FindIndex(x => x.Contains("Slot Level", StringComparison.OrdinalIgnoreCase)));
            if (slotsColumn < 0 || levelColumn < 0 || group["rows"] is not JsonArray rows)
            {
                continue;
            }
            int Cell(int level, int column)
            {
                var cell = (rows[level - 1] as JsonArray)?[column];
                var text = cell is JsonObject o ? o["value"]?.ToString() ?? "" : FiveEToolsRenderer.PlainText(cell?.ToString() ?? "");
                return int.TryParse(new string(text.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0;
            }
            int previousCount = 0, previousLevel = 0;
            for (var level = 1; level <= rows.Count; level++)
            {
                var (count, slotLevel) = (Cell(level, slotsColumn), Cell(level, levelColumn));
                if (count > previousCount)
                {
                    rules.Add(Stat($"{slug}:spellcasting:slots:count", (count - previousCount).ToString(), level));
                }
                if (slotLevel > previousLevel)
                {
                    rules.Add(Stat($"{slug}:spellcasting:slot", (slotLevel - previousLevel).ToString(), level));
                    if (previousLevel > 0)
                    {
                        rules.Add(Stat($"{slug}:spellcasting:slots:{previousLevel}", $"-{slug}:spellcasting:slots:count", level));
                    }
                    rules.Add(Stat($"{slug}:spellcasting:slots:{slotLevel}", $"{slug}:spellcasting:slots:count", level));
                }
                previousCount = Math.Max(previousCount, count);
                previousLevel = Math.Max(previousLevel, slotLevel);
            }
            return;
        }
    }

    /// <summary>Taking the class as an additional class: its ability score requirement and the proficiencies it gives.</summary>
    private XElement Multiclass(JsonObject cls, string multiclassId, string name, bool modern)
    {
        var multiclass = new XElement("multiclass", new XAttribute("id", multiclassId));
        var requirements = cls["multiclassing"]?["requirements"] as JsonObject;
        // the 2024 rule: 13 in the class's primary ability (one of them, when it has a choice)
        if (requirements is null && cls["primaryAbility"] is JsonArray primary && primary.Count > 0)
        {
            var abilities = primary.OfType<JsonObject>().SelectMany(p => p.Where(a => a.Value is JsonValue v && v.TryGetValue<bool>(out var yes) && yes).Select(a => a.Key)).Distinct().ToList();
            requirements = abilities.Count == 1
                ? new JsonObject { [abilities[0]] = 13 }
                : new JsonObject { ["or"] = new JsonArray(new JsonObject(abilities.Select(a => KeyValuePair.Create(a, (JsonNode?)JsonValue.Create(13))))) };
        }
        if (requirements is not null)
        {
            // {"int": 13} needs all; {"or": [{"str": 13, "dex": 13}]} needs one
            var all = requirements.Where(p => FiveEToolsText.Abilities.Contains(p.Key)).Select(p => (p.Key, Score: p.Value?.ToString())).ToList();
            var any = requirements["or"] is JsonArray or ? or.OfType<JsonObject>().SelectMany(o => o).Select(p => (p.Key, Score: p.Value?.ToString())).ToList() : [];
            var words = all.Select(a => $"{FiveEToolsText.AbilityName(a.Key)} {a.Score}").ToList();
            var terms = all.Select(a => $"[{a.Key}:{a.Score}]").ToList();
            if (any.Count > 0)
            {
                words.Add(string.Join(" or ", any.Select(a => $"{FiveEToolsText.AbilityName(a.Key)} {a.Score}")));
                terms.Add($"({string.Join("||", any.Select(a => $"[{a.Key}:{a.Score}]"))})");
            }
            if (words.Count > 0)
            {
                multiclass.Add(new XElement("prerequisite", string.Join(", ", words)));
                var expression = string.Join(",", terms);
                multiclass.Add(new XElement("requirements", _aurora.Exists("ID_INTERNAL_GRANTS_MULTICLASS_UNLOCKER") ? $"({expression})||ID_INTERNAL_GRANTS_MULTICLASS_UNLOCKER" : expression));
            }
        }

        var gained = cls["multiclassing"]?["proficienciesGained"] as JsonObject;
        var rules = new XElement("rules");
        if (_aurora.Exists("ID_INTERNAL_GRANT_MULTICLASS"))
        {
            rules.Add(Grant("Grants", "ID_INTERNAL_GRANT_MULTICLASS"));
        }
        if (gained is not null)
        {
            StartingProficiencies(rules, gained, $"{name} Multiclass", modern, null);
        }
        multiclass.Add(new XElement("setters", new XElement("set", new XAttribute("name", "multiclass proficiencies"),
            gained is null ? "—" : string.Join(", ", gained.Select(p => p.Key)))));
        multiclass.Add(rules);
        return multiclass;
    }

    private void Subclasses()
    {
        foreach (var subclass in Ordered(_json.Load("class/class-*.json", "subclass")))
        {
            var (name, source) = (Name(subclass), SourceOf(subclass));
            var shortName = FiveEToolsJson.Str(subclass, "shortName") ?? name;
            // 5etools' copies of the 2014 subclasses onto the 2024 classes: Aurora's subclass choices offer those already
            if (Skip(subclass, "subclass") || name.Length == 0 || subclass["_copiedFrom"] is not null || AuroraHas("subclass", subclass, shortName))
            {
                continue;
            }
            var (className, classSource) = (FiveEToolsJson.Str(subclass, "className") ?? "", FiveEToolsJson.Str(subclass, "classSource") ?? "PHB");
            var modern = IsModern(subclass);
            var support = SubclassSupport(className, classSource);
            if (support is null)
            {
                Report(ImportDiagnosticCode.SubclassClassMissing, $"{name} is a subclass of {className} ({BookName(classSource)}), a class neither Aurora nor 5etools has here; it was left out.",
                    null, name, $"{className}|{classSource}");
                continue;
            }

            var id = IdOf("SUBCLASS", source, className, name);
            var features = new List<JsonObject>();
            foreach (var reference in (subclass["subclassFeatures"] is JsonArray list ? list : []).Select(r => r?.ToString()).OfType<string>())
            {
                if (SubclassFeature(reference) is { } found)
                {
                    features.Add(found);
                }
                else
                {
                    Report(ImportDiagnosticCode.ClassFeatureMissing, $"{name} lists the feature {reference}, which is not in the 5etools data; it was left out.", id, name, reference);
                }
            }
            // the first feature, named like the subclass, is its introduction
            var introduction = features.FirstOrDefault(f => Name(f) == shortName || Name(f) == name);
            var element = NewElement("subclasses", name, "Archetype", source, id, modern,
                Description(introduction is null ? null : new JsonArray(Plain(introduction).ToArray())));
            element.Add(new XElement("supports", support));
            element.Add(new XElement("sheet", new XAttribute("display", "false")));
            var rules = RulesOf(element);

            foreach (var feature in features)
            {
                var level = FiveEToolsJson.Int(feature, "level") ?? 1;
                // the introduction's text is the subclass's; what it lists are features of their own
                List<JsonObject> parts = feature == introduction ? [.. References(feature)] : [feature, .. References(feature)];
                foreach (var part in parts)
                {
                    var partName = Name(part);
                    var partId = IdOf("SUBCLASS_FEATURE", source, className, name, partName, level.ToString());
                    var partElement = NewElement("subclass features", $"Level {level}: {partName}", "Archetype Feature", source, partId, modern,
                        Description(new JsonArray(Plain(part).ToArray())));
                    partElement.Add(Sheet(partElement.Element("description")!, partName));
                    rules.Add(Grant("Archetype Feature", partId, level));
                }
            }

            SubclassSpells(element, subclass, className, name, source, modern);
            if (FiveEToolsJson.Str(subclass, "spellcastingAbility") is { } ability && FiveEToolsJson.Str(subclass, "casterProgression") is not null)
            {
                // a subclass that casts (the Eldritch Knight kind): its own spellcasting, named after it
                var castingId = IdOf("SUBCLASS_FEATURE", source, className, name, "Spellcasting");
                var casting = NewElement("subclass features", $"{name} Spellcasting", "Archetype Feature", source, castingId, modern, Description(null));
                casting.Element("description")!.AddFirst(new XElement("p", $"You cast spells as a {name}; see the subclass's description."));
                Spellcasting(casting, subclass, name, ability, false);
                rules.Add(Grant("Archetype Feature", castingId, features.Select(f => FiveEToolsJson.Int(f, "level") ?? 3).DefaultIfEmpty(3).Min()));
            }
            Tidy(element);
        }

        // the entries of a feature without the features it refers to, and those referred to
        static IEnumerable<JsonNode?> Plain(JsonObject feature) =>
            (feature["entries"] as JsonArray ?? []).Where(e => FiveEToolsJson.Str(e, "type") is not ("refSubclassFeature" or "refClassFeature")).Select(e => e?.DeepClone());
    }

    private IEnumerable<JsonObject> References(JsonObject feature) =>
        (feature["entries"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(e => FiveEToolsJson.Str(e, "type") == "refSubclassFeature")
            .Select(e => SubclassFeature(FiveEToolsJson.Str(e, "subclassFeature") ?? ""))
            .OfType<JsonObject>();

    /// <summary>
    /// What the class's subclass choice asks for: "Cleric Subclass" for the 2024 Cleric, "Arcane Tradition" for the
    /// 2014 Wizard; found on the class or the feature that makes the choice. A class neither Aurora nor this
    /// translation has gets nothing (its subclasses could not be chosen).
    /// </summary>
    private string? SubclassSupport(string className, string classSource)
    {
        if (_classes.TryGetValue($"{className}|{classSource}", out var generated))
        {
            return generated.SubclassSupport;
        }
        var modern = classSource == "XPHB" || _books.TryGetValue(classSource, out var book) && book.Published >= new DateTime(2024, 9, 1);
        var cls = _aurora.Find("Class", className, SourceName(classSource), modern).FirstOrDefault(c => c.Type == "Class");
        if (cls is null)
        {
            return null;
        }
        var owners = new[] { cls.Xml }.Concat(cls.Xml.Element("rules")?.Elements("grant")
            .Select(g => _aurora.Get((string?)g.Attribute("id") ?? "")?.Xml).OfType<XElement>() ?? []);
        var select = owners.SelectMany(o => o.Element("rules")?.Elements("select") ?? [])
            .FirstOrDefault(s => (string?)s.Attribute("type") is "Archetype" or "SubClass");
        return SupportToken((string?)select?.Attribute("supports"));
    }

    /// <summary>
    /// Spells a subclass always has prepared (Domain Spells, Oath Spells) as grants, the way Aurora's 2024 subclasses
    /// write them; spells it adds to the class's list (a 2014 patron's expanded list) as an extension of that list.
    /// </summary>
    private void SubclassSpells(XElement subclass, JsonObject data, string className, string name, string source, bool modern)
    {
        if (data["additionalSpells"] is not JsonArray alternatives || alternatives.FirstOrDefault() is not JsonObject set)
        {
            return;
        }
        var prepared = Levels(set["prepared"]).ToList();
        var expanded = set["expanded"] is JsonObject e ? e.Select(p => p.Value).OfType<JsonArray>().SelectMany(a => a).ToList() : [];
        if (prepared.Count == 0 && expanded.Count == 0)
        {
            return;
        }
        var first = prepared.Select(p => p.Level).DefaultIfEmpty(1).Min();
        var id = IdOf("SUBCLASS_FEATURE", source, className, name, "Spells");
        var feature = NewElement("subclass features", $"Level {first}: {name} Spells", "Archetype Feature", source, id, modern, Description(null));
        feature.Element("description")!.AddFirst(new XElement("p", $"The spells of the {name}; see the subclass's description."));
        feature.Add(new XElement("sheet", new XAttribute("display", "false")));
        var rules = RulesOf(feature);
        foreach (var (level, spells) in prepared)
        {
            foreach (var spell in spells.OfType<JsonValue>().Select(s => s.ToString()))
            {
                if (SpellId(spell, modern) is { } spellId)
                {
                    rules.Add(new XElement("grant", new XAttribute("type", "Spell"), new XAttribute("id", spellId), new XAttribute("level", level),
                        new XAttribute("spellcasting", className), new XAttribute("prepared", "true")));
                }
            }
        }
        var extends = expanded.OfType<JsonValue>().Select(s => SpellId(s.ToString(), modern)).OfType<string>().Distinct().ToList();
        if (extends.Count > 0)
        {
            feature.Add(new XElement("spellcasting", new XAttribute("name", className), new XAttribute("extend", "true"),
                extends.Select(s => new XElement("extend", s))));
        }
        Tidy(feature);
        RulesOf(subclass).Add(Grant("Archetype Feature", id, first));
    }
}

internal static class FiveEToolsXmlExtensions
{
    /// <summary>A spell choice that belongs to a spellcasting ("Cantrip (Wizard)" is the Wizard's).</summary>
    public static XElement WithSpellcasting(this XElement select, string name)
    {
        select.Add(new XAttribute("spellcasting", name));
        return select;
    }
}
