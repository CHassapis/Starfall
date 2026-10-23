using Starlights.Modules.Characters.Domain.Registrations;

namespace Starlights.Modules.Characters.Services.Calculation;

/// <summary>
/// What the statistics calculator did, written down as it does it: every value a rule put into a statistic (and every
/// one it considered and left out), the halves it made, how each ability score came out, and how each skill and
/// saving throw was added up. The calculator only writes to it when one is given (every call is <c>trace?.…</c>), so
/// a calculation with and without a recorder runs the same code and gives the same figures.
/// <see cref="CalculationTraceBuilder"/> turns it into traces.
/// </summary>
public sealed class StatisticsTraceRecorder
{
    private readonly Dictionary<string, List<RecordedValue>> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecordedVariant> _variants = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecordedAbility> _abilities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecordedCheck> _checks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyCollection<string>> _absent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyCollection<string>> _unresolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A value for a statistic: put in, or considered and left out.</summary>
    public void Value(string statistic, RecordedValue value)
    {
        var key = Normalize(statistic);
        if (!_values.TryGetValue(key, out var list))
        {
            _values[key] = list = [];
        }
        list.Add(value);
    }

    /// <summary>A half of another statistic (":half", ":half:up"), as it was made.</summary>
    public void Variant(string statistic, RecordedVariant variant) => _variants[Normalize(statistic)] = variant;

    public void Ability(RecordedAbility ability) => _abilities[ability.Slug] = ability;

    /// <summary>A skill ("skill:athletics") or saving throw ("saving-throw:strength") as it was added up.</summary>
    public void Check(string key, RecordedCheck check) => _checks[key] = check;

    /// <summary>A statistic left out because what it refers to is given to the character by nothing.</summary>
    public void Absent(string statistic, IReadOnlyCollection<string> dependencies) => _absent[Normalize(statistic)] = dependencies;

    /// <summary>A statistic that could not be finished: what it refers to never was.</summary>
    public void Unresolved(string statistic, IReadOnlyCollection<string> dependencies) => _unresolved[Normalize(statistic)] = dependencies;

    public IReadOnlyList<RecordedValue> ValuesOf(string statistic) =>
        _values.TryGetValue(Normalize(statistic), out var list) ? list : [];

    public RecordedVariant? VariantOf(string statistic) => _variants.GetValueOrDefault(Normalize(statistic));

    public RecordedAbility? AbilityOf(string slug) => _abilities.GetValueOrDefault(slug);

    public RecordedCheck? CheckOf(string key) => _checks.GetValueOrDefault(key);

    public IReadOnlyCollection<string>? AbsentBecause(string statistic) => _absent.GetValueOrDefault(Normalize(statistic));

    public IReadOnlyCollection<string>? UnresolvedBecause(string statistic) => _unresolved.GetValueOrDefault(Normalize(statistic));

    public IEnumerable<string> Statistics => _values.Keys;

    // the statistics collection ignores a leading '-' in a name; so does the trace
    private static string Normalize(string name) => name.StartsWith('-') ? name[1..] : name;
}

/// <summary>
/// A value a rule (or the character) put into a statistic, or one that was considered and left out.
/// </summary>
/// <param name="Value">What went in (after the rule's own minimum and maximum).</param>
/// <param name="Label">The name the statistic shows for it (the element's).</param>
/// <param name="Kind">Where it comes from.</param>
/// <param name="Rule">The rule, for a rule's value.</param>
/// <param name="Before">The value before the rule's minimum or maximum changed it; null when they did not.</param>
/// <param name="Included">False when it was left out.</param>
/// <param name="Reason">Why it was left out.</param>
/// <param name="Detail">What counted instead.</param>
public sealed record RecordedValue(
    int Value,
    string Label,
    TraceSourceKind Kind,
    RegistrationStatisticRule? Rule = null,
    int? Before = null,
    bool Included = true,
    TraceExclusionReason? Reason = null,
    string? Detail = null);

/// <summary>A half of a statistic: the statistic's total then, halved, rounded down or up.</summary>
public sealed record RecordedVariant(string Origin, int OriginValue, bool RoundUp, int Value);

/// <summary>
/// An ability score as the abilities processor worked it out: the base, the bonuses (when their statistic was
/// complete), the maximum (20 unless raised) and what an item sets it to, then the score and modifier the character
/// has afterwards.
/// </summary>
public sealed record RecordedAbility(
    string Name,
    string Slug,
    int Base,
    int Bonuses,
    bool BonusesCounted,
    int Maximum,
    int MaximumExtra,
    int? SetTo,
    int Score,
    int Modifier);

/// <summary>
/// A skill or saving throw as its post-processor added it up: the ability modifier, the proficiency and misc
/// statistics (their names and totals), and the bonus the character has afterwards.
/// </summary>
public sealed record RecordedCheck(
    string Name,
    string? Ability,
    int AbilityModifier,
    string ProficiencyStatistic,
    int Proficiency,
    string MiscStatistic,
    int Misc,
    int Final);
