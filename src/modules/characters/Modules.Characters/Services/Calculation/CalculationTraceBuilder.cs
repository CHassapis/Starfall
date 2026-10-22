using System.Security.Cryptography;
using System.Text;
using Starlights.Modules.Characters.Domain.Registrations;
using Starlights.Modules.Characters.Services.Magic;
using Starlights.Modules.Characters.Services.Statistics;

namespace Starlights.Modules.Characters.Services.Calculation;

/// <summary>
/// Turns what a traced calculation wrote down (<see cref="StatisticsTraceRecorder"/>) into the trace of one value.
/// Every final value is the one the calculation produced (the statistic's total, the character's ability score, the
/// skill's bonus, the spellcasting's attack); the steps are the recorded values that made it. A trace whose steps do
/// not add up to its final value says so in its notes (the reference character tests check that none does).
/// </summary>
public sealed class CalculationTraceBuilder
{
    private const int MaxDepth = 8;

    private static readonly Dictionary<string, string> AbilityByAbbreviation = new(StringComparer.OrdinalIgnoreCase)
    {
        ["STR"] = "strength", ["DEX"] = "dexterity", ["CON"] = "constitution",
        ["INT"] = "intelligence", ["WIS"] = "wisdom", ["CHA"] = "charisma",
    };

    private readonly StatisticsTraceRecorder _recorder;
    private readonly StatisticValuesGroupCollection _statistics;
    private readonly Dictionary<RegistrationId, Registration> _registrations;
    private readonly SpellcastingFactsModel? _spellcasting;

    public CalculationTraceBuilder(StatisticsTraceRecorder recorder, StatisticValuesGroupCollection statistics, IEnumerable<Registration> registrations, SpellcastingFactsModel? spellcasting = null)
    {
        _recorder = recorder;
        _statistics = statistics;
        _registrations = registrations.ToDictionary(r => r.Id);
        _spellcasting = spellcasting;
    }

    /// <summary>The trace of a server key (see <see cref="CalculationTraceKeys"/>), or why there is none.</summary>
    public (TraceStatus Status, CalculationTrace? Trace, string? Reason) Build(string key)
    {
        if (!CalculationTraceKeys.IsServerKey(key))
        {
            return CalculationTraceKeys.IsWebKey(key)
                ? (TraceStatus.Unsupported, null, "The web app works this value out and traces it itself.")
                : (TraceStatus.Unsupported, null, "Not a value the server traces.");
        }
        var (trace, reason) = Trace(key, depth: 0, path: []);
        return trace is null ? (TraceStatus.Missing, null, reason) : (TraceStatus.Traced, trace, null);
    }

    /// <summary>The rules in a trace and its inputs, for the content it was worked out from.</summary>
    public IReadOnlyList<RegistrationStatisticRule> RulesIn(CalculationTrace trace)
    {
        var ids = new HashSet<Guid>();
        void Walk(CalculationTrace t)
        {
            foreach (var step in t.Steps)
            {
                if (step.Source?.RuleId is { } id) ids.Add(id);
                if (step.Input is not null) Walk(step.Input);
            }
            foreach (var excluded in t.Excluded)
            {
                if (excluded.Source?.RuleId is { } id) ids.Add(id);
            }
        }
        Walk(trace);
        return _registrations.Values.SelectMany(r => r.StatisticRules).Where(r => ids.Contains(r.AssociatedStatisticRuleId.Value)).ToList();
    }

    /// <summary>
    /// A fingerprint of the content a trace was worked out from: its rules' elements, statistics, values, bonus names
    /// and limits, and the elements' sources. Element and rule ids are left out: the server's own elements (abilities,
    /// skills) get ids of their own on each server.
    /// </summary>
    public static string Fingerprint(IEnumerable<(RegistrationStatisticRule Rule, string Element, string? Source)> rules)
    {
        var lines = rules
            .Select(r => string.Join('|', r.Source ?? "", r.Element, r.Rule.Name, r.Rule.Value, r.Rule.StackingBonus ?? "", r.Rule.MinimumValue?.ToString() ?? "", r.Rule.MaximumValue?.ToString() ?? ""))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return "sha256:" + Convert.ToHexStringLower(hash)[..32];
    }

    public Registration? RegistrationOf(RegistrationStatisticRule rule) => _registrations.GetValueOrDefault(rule.ParentRegistrationId);

    // ---- the values

    private (CalculationTrace? Trace, string? Reason) Trace(string key, int depth, HashSet<string> path)
    {
        if (depth > MaxDepth || !path.Add(key))
        {
            return (null, depth > MaxDepth ? "Nested too deep to trace." : "Refers to itself.");
        }
        try
        {
            var (kind, name) = Split(key);
            return kind switch
            {
                "ability-score" => AbilityScore(name, depth, path),
                "ability-modifier" => AbilityModifier(name, depth, path),
                "saving-throw" => Check(key, $"{Capitalize(name)} saving throw", depth, path),
                "skill" => Check(key, null, depth, path),
                "proficiency-bonus" => Statistic("proficiency", key, "Proficiency bonus", depth, path),
                "spell-attack" => Spell(name, attack: true, depth, path),
                "spell-save-dc" => Spell(name, attack: false, depth, path),
                "statistic" => StatisticKey(name, depth, path),
                _ => (null, "Not a value the server traces."),
            };
        }
        finally
        {
            path.Remove(key);
        }
    }

    private static (string Kind, string Name) Split(string key)
    {
        var colon = key.IndexOf(':');
        return colon < 0 ? (key, "") : (key[..colon], key[(colon + 1)..]);
    }

    /// <summary>The key a statistic is traced under: its own when it has one (an ability score, the proficiency bonus).</summary>
    private static string KeyOf(string statistic)
    {
        var name = statistic.StartsWith('-') ? statistic[1..] : statistic;
        foreach (var ability in CalculationTraceKeys.Abilities)
        {
            if (string.Equals(name, $"{ability}:score", StringComparison.OrdinalIgnoreCase)) return $"ability-score:{ability}";
            if (string.Equals(name, $"{ability}:modifier", StringComparison.OrdinalIgnoreCase)) return $"ability-modifier:{ability}";
        }
        return string.Equals(name, "proficiency", StringComparison.OrdinalIgnoreCase) ? "proficiency-bonus" : $"statistic:{name.ToLowerInvariant()}";
    }

    private (CalculationTrace?, string?) StatisticKey(string name, int depth, HashSet<string> path)
    {
        var canonical = KeyOf(name);
        if (canonical != $"statistic:{name}")
        {
            return Trace(canonical, depth, path);
        }
        if (_recorder.VariantOf(name) is { } variant)
        {
            return Variant(name, variant, depth, path);
        }
        return Statistic(name, canonical, _statistics.TryGetGroup(name, out var g) ? g.DisplayName ?? name : name, depth, path);
    }

    /// <summary>A statistic: the values rules put into it, in the order the calculation added them.</summary>
    private (CalculationTrace?, string?) Statistic(string name, string key, string label, int depth, HashSet<string> path)
    {
        if (!_statistics.TryGetGroup(name, out var group) || !group.HasStatisticValues())
        {
            if (_recorder.AbsentBecause(name) is { } absent)
            {
                return (null, $"Left out: it refers to {string.Join(", ", absent)}, which nothing gives this character.");
            }
            if (_recorder.UnresolvedBecause(name) is { } unresolved)
            {
                return (null, $"Not worked out: it refers to {string.Join(", ", unresolved)}, which was never worked out.");
            }
            return (null, $"The character has no {name}.");
        }

        var steps = new List<TraceStep>();
        var excluded = new List<TraceExclusion>();
        decimal running = 0;
        foreach (var value in _recorder.ValuesOf(name))
        {
            var source = SourceOf(value);
            if (!value.Included)
            {
                excluded.Add(new TraceExclusion { Reason = value.Reason ?? TraceExclusionReason.Stacking, Value = value.Value, Label = value.Label, Source = source, Detail = value.Detail });
                continue;
            }
            var first = steps.Count == 0;
            running = first ? value.Value : running + value.Value;
            steps.Add(new TraceStep
            {
                Operation = first ? TraceOperation.Start : TraceOperation.Add,
                Value = value.Value,
                Result = running,
                Label = value.Label,
                Source = source,
                Input = RuleInput(value, depth, path),
            });
        }
        return (Finish(new CalculationTrace { Key = key, Label = label, Final = group.Sum(), Steps = steps, Excluded = excluded }), null);
    }

    /// <summary>How a rule's value was worked out, when that is more than its number: the statistic it refers to, its own limits.</summary>
    private CalculationTrace? RuleInput(RecordedValue value, int depth, HashSet<string> path)
    {
        var rule = value.Rule;
        if (rule is null)
        {
            return null;
        }
        var refers = rule.HasReferenceValue() ? Trace(KeyOf(rule.Value), depth + 1, path).Trace : null;
        if (value.Before is not { } before)
        {
            return refers;
        }
        // the rule's own minimum or maximum changed the value
        var limited = value.Value < before;
        return new CalculationTrace
        {
            Key = refers?.Key ?? $"rule:{rule.AssociatedStatisticRuleId.Value}",
            Label = value.Label,
            Final = value.Value,
            Steps =
            [
                new TraceStep { Operation = TraceOperation.Start, Value = before, Result = before, Label = refers?.Label ?? rule.Value, Input = refers },
                new TraceStep
                {
                    Operation = limited ? TraceOperation.Minimum : TraceOperation.Maximum,
                    Value = value.Value,
                    Result = value.Value,
                    Label = limited ? "The rule's maximum" : "The rule's minimum",
                    Source = SourceOf(value),
                },
            ],
        };
    }

    private TraceSource SourceOf(RecordedValue value)
    {
        if (value.Rule is not { } rule)
        {
            return new TraceSource { Kind = value.Kind, Name = value.Label };
        }
        var registration = RegistrationOf(rule);
        return new TraceSource
        {
            Kind = TraceSourceKind.Rule,
            Name = registration?.AssociatedElementName ?? value.Label,
            ElementType = registration?.AssociatedElementType,
            ElementId = registration?.AssociatedElementId.Value,
            RegistrationId = rule.ParentRegistrationId.Value,
            RuleId = rule.AssociatedStatisticRuleId.Value,
            Bonus = rule.StackingBonus,
            Refers = rule.HasReferenceValue() ? rule.Value : null,
        };
    }

    /// <summary>A half of a statistic, as the calculation made it: its total then, halved and rounded.</summary>
    private (CalculationTrace?, string?) Variant(string name, RecordedVariant variant, int depth, HashSet<string> path)
    {
        var origin = Trace(KeyOf(variant.Origin), depth + 1, path).Trace;
        var half = variant.OriginValue / 2m;
        var rounding = variant.RoundUp ? TraceOperation.RoundUp : variant.OriginValue < 0 ? TraceOperation.RoundTowardZero : TraceOperation.RoundDown;
        return (Finish(new CalculationTrace
        {
            Key = $"statistic:{name.ToLowerInvariant()}",
            Label = $"Half of {origin?.Label ?? variant.Origin}{(variant.RoundUp ? ", rounded up" : "")}",
            Final = variant.Value,
            Steps =
            [
                new TraceStep { Operation = TraceOperation.Start, Value = variant.OriginValue, Result = variant.OriginValue, Label = origin?.Label ?? variant.Origin, Source = new TraceSource { Kind = TraceSourceKind.Calculated, Refers = variant.Origin }, Input = origin },
                new TraceStep { Operation = TraceOperation.Divide, Value = 2, Result = half, Label = "Half", Source = Fixed("Half") },
                new TraceStep { Operation = rounding, Value = half, Result = variant.Value, Label = variant.RoundUp ? "Rounded up" : "Rounded down", Source = Fixed("Rounding") },
            ],
        }), null);
    }

    /// <summary>An ability score as the abilities processor worked it out (see AbilitiesGroupProcessor).</summary>
    private (CalculationTrace?, string?) AbilityScore(string ability, int depth, HashSet<string> path)
    {
        if (_recorder.AbilityOf(ability) is not { } a)
        {
            return (null, $"The character has no {ability} score.");
        }
        var steps = new List<TraceStep>
        {
            new() { Operation = TraceOperation.Start, Value = a.Base, Result = a.Base, Label = "Base score", Source = new TraceSource { Kind = TraceSourceKind.UserSupplied, Name = "Base score" } },
        };
        var excluded = new List<TraceExclusion>();
        decimal running = a.Base;
        var hasBonuses = _statistics.TryGetGroup(a.Slug, out var bonuses) && bonuses.HasStatisticValues();
        if (a.BonusesCounted && hasBonuses)
        {
            running += a.Bonuses;
            steps.Add(new TraceStep { Operation = TraceOperation.Add, Value = a.Bonuses, Result = running, Label = "Increases", Source = Calculated(a.Slug), Input = Trace($"statistic:{a.Slug}", depth + 1, path).Trace });
        }
        else if (hasBonuses)
        {
            excluded.Add(new TraceExclusion { Reason = TraceExclusionReason.UnresolvedDependency, Value = bonuses!.Sum(), Label = "Increases", Detail = "Their statistic was not worked out completely, so they were not added." });
        }

        var maximum = a.Maximum + a.MaximumExtra;
        if (running > maximum)
        {
            running = maximum;
            steps.Add(new TraceStep { Operation = TraceOperation.Minimum, Value = maximum, Result = running, Label = a.MaximumExtra != 0 ? $"Maximum ({a.Maximum} + {a.MaximumExtra})" : "Maximum", Source = Calculated($"{a.Slug}:max"), Input = MaximumInput(a, depth, path) });
            if (a.Base > maximum)
            {
                running = a.Base;
                steps.Add(new TraceStep { Operation = TraceOperation.Maximum, Value = a.Base, Result = running, Label = "The base score is already above the maximum", Source = new TraceSource { Kind = TraceSourceKind.UserSupplied, Name = "Base score" } });
            }
        }
        if (a.SetTo is { } setTo)
        {
            var setInput = Trace($"statistic:{a.Slug}:score:set", depth + 1, path).Trace;
            var setter = setInput?.Steps.FirstOrDefault(s => s.Value == setTo)?.Source;
            if (setTo > running)
            {
                running = setTo;
                steps.Add(new TraceStep { Operation = TraceOperation.Replace, Value = setTo, Result = running, Label = $"Set to {setTo} by {setter?.Name ?? "an item"}", Source = setter, Input = setInput });
            }
            else
            {
                excluded.Add(new TraceExclusion { Reason = TraceExclusionReason.SetNotHigher, Value = setTo, Label = setter?.Name ?? "Set score", Source = setter, Detail = $"Sets the score to {setTo}, which is not higher than {running}." });
            }
        }
        return (Finish(new CalculationTrace { Key = $"ability-score:{ability}", Label = $"{a.Name} score", Final = a.Score, Steps = steps, Excluded = excluded }), null);
    }

    private CalculationTrace? MaximumInput(RecordedAbility a, int depth, HashSet<string> path)
    {
        var max = Trace($"statistic:{a.Slug}:max", depth + 1, path).Trace;
        var extra = a.MaximumExtra != 0 ? Trace($"statistic:{a.Slug}:max:extra", depth + 1, path).Trace : null;
        if (extra is null)
        {
            return max;
        }
        return new CalculationTrace
        {
            Key = $"statistic:{a.Slug}:max",
            Label = $"{a.Name} maximum",
            Final = a.Maximum + a.MaximumExtra,
            Steps =
            [
                new TraceStep { Operation = TraceOperation.Start, Value = a.Maximum, Result = a.Maximum, Label = "Maximum", Source = Calculated($"{a.Slug}:max"), Input = max },
                new TraceStep { Operation = TraceOperation.Add, Value = a.MaximumExtra, Result = a.Maximum + a.MaximumExtra, Label = "Raised by", Source = Calculated($"{a.Slug}:max:extra"), Input = extra },
            ],
        };
    }

    /// <summary>An ability modifier: (score - 10) / 2, rounded down.</summary>
    private (CalculationTrace?, string?) AbilityModifier(string ability, int depth, HashSet<string> path)
    {
        if (_recorder.AbilityOf(ability) is not { } a)
        {
            return (null, $"The character has no {ability} score.");
        }
        var less = a.Score - 10m;
        var half = less / 2m;
        return (Finish(new CalculationTrace
        {
            Key = $"ability-modifier:{ability}",
            Label = $"{a.Name} modifier",
            Final = a.Modifier,
            Steps =
            [
                new TraceStep { Operation = TraceOperation.Start, Value = a.Score, Result = a.Score, Label = $"{a.Name} score", Source = Calculated($"{ability}:score"), Input = Trace($"ability-score:{ability}", depth + 1, path).Trace },
                new TraceStep { Operation = TraceOperation.Add, Value = -10, Result = less, Label = "Less 10", Source = Fixed("Ability modifier") },
                new TraceStep { Operation = TraceOperation.Divide, Value = 2, Result = half, Label = "Half", Source = Fixed("Ability modifier") },
                new TraceStep { Operation = TraceOperation.RoundDown, Value = half, Result = Math.Floor(half), Label = "Rounded down", Source = Fixed("Rounding") },
            ],
        }), null);
    }

    /// <summary>A skill or saving throw: the ability modifier, plus its proficiency and other bonuses.</summary>
    private (CalculationTrace?, string?) Check(string key, string? label, int depth, HashSet<string> path)
    {
        if (_recorder.CheckOf(key) is not { } c)
        {
            return (null, $"The character has no {Split(key).Name.Replace('-', ' ')}.");
        }
        var ability = c.Ability is { } abbreviation && AbilityByAbbreviation.TryGetValue(abbreviation, out var full) ? full : null;
        var steps = new List<TraceStep>
        {
            new()
            {
                Operation = TraceOperation.Start,
                Value = c.AbilityModifier,
                Result = c.AbilityModifier,
                Label = ability is null ? "Ability modifier" : $"{Capitalize(ability)} modifier",
                Source = Calculated(ability is null ? "ability modifier" : $"{ability}:modifier"),
                Input = ability is null ? null : Trace($"ability-modifier:{ability}", depth + 1, path).Trace,
            },
        };
        decimal running = c.AbilityModifier;
        if (Has(c.ProficiencyStatistic))
        {
            running += c.Proficiency;
            steps.Add(new TraceStep { Operation = TraceOperation.Add, Value = c.Proficiency, Result = running, Label = "Proficiency", Source = Calculated(c.ProficiencyStatistic), Input = Trace(KeyOf(c.ProficiencyStatistic), depth + 1, path).Trace });
        }
        if (Has(c.MiscStatistic))
        {
            running += c.Misc;
            steps.Add(new TraceStep { Operation = TraceOperation.Add, Value = c.Misc, Result = running, Label = "Other bonuses", Source = Calculated(c.MiscStatistic), Input = Trace(KeyOf(c.MiscStatistic), depth + 1, path).Trace });
        }
        return (Finish(new CalculationTrace { Key = key, Label = label ?? c.Name, Final = c.Final, Steps = steps }), null);
    }

    /// <summary>A spellcasting's spell attack or save DC, from the parts SpellcastingFacts added up.</summary>
    private (CalculationTrace?, string?) Spell(string slug, bool attack, int depth, HashSet<string> path)
    {
        if (_spellcasting is null)
        {
            return (null, "The character's spellcasting was not worked out.");
        }
        var caster = _spellcasting.Casters.FirstOrDefault(c => SpellcastingFacts.Slug(c.Name) == slug);
        if (caster is null)
        {
            return (null, $"The character has no {slug} spellcasting.");
        }
        var steps = new List<TraceStep>();
        decimal running = 0;
        void Add(decimal value, string label, TraceSource source, CalculationTrace? input = null)
        {
            var first = steps.Count == 0;
            running = first ? value : running + value;
            steps.Add(new TraceStep { Operation = first ? TraceOperation.Start : TraceOperation.Add, Value = value, Result = running, Label = label, Source = source, Input = input });
        }
        if (!attack)
        {
            Add(8, "Base", Fixed("Spell save DC"));
        }
        Add(_spellcasting.ProficiencyBonus, "Proficiency bonus", Calculated("proficiency"), Trace("proficiency-bonus", depth + 1, path).Trace);
        if (caster.Ability is { } ability)
        {
            var key = ability.ToLowerInvariant();
            Add(caster.AbilityModifier, $"{ability} modifier", Calculated($"{key}:modifier"), Trace($"ability-modifier:{key}", depth + 1, path).Trace);
        }
        // the same statistics SpellcastingFacts adds, when complete: the spellcasting's own, then every spellcasting's
        foreach (var statistic in new[] { $"{slug}:spellcasting:{(attack ? "attack" : "dc")}", $"spellcasting:{(attack ? "attack" : "dc")}" })
        {
            if (_statistics.TryGetGroup(statistic, out var group) && group.IsCompleted && group.HasStatisticValues())
            {
                Add(group.Sum(), attack ? "Spell attack bonuses" : "Save DC bonuses", Calculated(statistic), Trace($"statistic:{statistic}", depth + 1, path).Trace);
            }
        }
        return (Finish(new CalculationTrace
        {
            Key = $"{(attack ? "spell-attack" : "spell-save-dc")}:{slug}",
            Label = $"{caster.Name} {(attack ? "spell attack" : "spell save DC")}",
            Final = attack ? caster.Attack : caster.Dc,
            Steps = steps,
        }), null);
    }

    private bool Has(string statistic) => _statistics.TryGetGroup(statistic, out var group) && group.HasStatisticValues();

    private static TraceSource Calculated(string statistic) => new() { Kind = TraceSourceKind.Calculated, Refers = statistic };

    private static TraceSource Fixed(string name) => new() { Kind = TraceSourceKind.Fixed, Name = name };

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Notes a trace whose steps do not come to its final value (part of the calculation was not written down).</summary>
    private static CalculationTrace Finish(CalculationTrace trace)
    {
        var replayed = Replay(trace.Steps);
        return replayed == trace.Final
            ? trace
            : trace with { Notes = [.. trace.Notes, $"The steps come to {replayed}, not {trace.Final}: part of how this value was worked out is not in the trace."] };
    }

    /// <summary>The value the steps come to, done one after another.</summary>
    public static decimal Replay(IEnumerable<TraceStep> steps)
    {
        decimal value = 0;
        foreach (var step in steps)
        {
            value = step.Operation switch
            {
                TraceOperation.Start or TraceOperation.Replace => step.Value,
                TraceOperation.Add => value + step.Value,
                TraceOperation.Maximum => Math.Max(value, step.Value),
                TraceOperation.Minimum => Math.Min(value, step.Value),
                TraceOperation.Multiply => value * step.Value,
                TraceOperation.Divide => step.Value == 0 ? value : value / step.Value,
                TraceOperation.RoundDown => Math.Floor(value),
                TraceOperation.RoundUp => Math.Ceiling(value),
                TraceOperation.RoundTowardZero => Math.Truncate(value),
                _ => value,
            };
        }
        return value;
    }
}
