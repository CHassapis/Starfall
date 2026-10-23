using System.Text.Json.Serialization;

namespace Starlights.Modules.Characters.Services.Calculation;

/// <summary>
/// The calculation trace contract: how a figure on the sheet was worked out, step by step, with what was left out
/// and why. The server traces the figures it calculates (GET /api/characters/{id}/calculation/trace); the web app
/// traces the ones it works out itself (armor class, hit points, attacks) in the same shape. Version 1; a change that
/// is not backwards compatible raises the version, and a request for a version the server does not have is refused
/// with "unsupported-version".
/// </summary>
public static class CalculationTraceContract
{
    public const int Version = 1;
}

/// <summary>What a step does to the running value.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TraceOperation>))]
public enum TraceOperation
{
    /// <summary>The value starts here.</summary>
    [JsonStringEnumMemberName("start")] Start,

    /// <summary>Adds the step's value (negative values subtract).</summary>
    [JsonStringEnumMemberName("add")] Add,

    /// <summary>The step's value takes the place of the running value.</summary>
    [JsonStringEnumMemberName("replace")] Replace,

    /// <summary>The higher of the running value and the step's value.</summary>
    [JsonStringEnumMemberName("maximum")] Maximum,

    /// <summary>The lower of the running value and the step's value (a cap).</summary>
    [JsonStringEnumMemberName("minimum")] Minimum,

    [JsonStringEnumMemberName("multiply")] Multiply,

    /// <summary>Divides by the step's value; a rounding step follows when the result is not whole.</summary>
    [JsonStringEnumMemberName("divide")] Divide,

    [JsonStringEnumMemberName("round-down")] RoundDown,
    [JsonStringEnumMemberName("round-up")] RoundUp,

    /// <summary>Drops the fraction (rounds toward zero): integer division of a negative value.</summary>
    [JsonStringEnumMemberName("round-toward-zero")] RoundTowardZero,
}

/// <summary>Why a value that was considered did not count.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TraceExclusionReason>))]
public enum TraceExclusionReason
{
    /// <summary>A bonus of the same kind counts once: a higher (or earlier, on a tie) one of that kind counted.</summary>
    [JsonStringEnumMemberName("stacking")] Stacking,

    /// <summary>The better of two alternatives counted (a finesse weapon's Strength or Dexterity, armor class calculations).</summary>
    [JsonStringEnumMemberName("lower-alternative")] LowerAlternative,

    /// <summary>A value the score is set to (an item's) was not higher than the score.</summary>
    [JsonStringEnumMemberName("set-not-higher")] SetNotHigher,

    /// <summary>A condition of the value was not met (no shield, wearing armor).</summary>
    [JsonStringEnumMemberName("condition-not-met")] ConditionNotMet,

    /// <summary>It refers to a statistic nothing gives the character.</summary>
    [JsonStringEnumMemberName("unresolved-dependency")] UnresolvedDependency,
}

/// <summary>Where a step's value comes from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TraceSourceKind>))]
public enum TraceSourceKind
{
    /// <summary>A statistic rule of one of the character's elements (a class feature, an item, the species).</summary>
    [JsonStringEnumMemberName("rule")] Rule,

    /// <summary>Entered by the player: a base ability score, rolled hit points, an attack written by hand. Shown as entered, not worked out.</summary>
    [JsonStringEnumMemberName("user-supplied")] UserSupplied,

    /// <summary>The character's level or a class level.</summary>
    [JsonStringEnumMemberName("character")] Character,

    /// <summary>A number of the rules themselves (the 8 of a save DC, the 10 of an ability modifier).</summary>
    [JsonStringEnumMemberName("fixed")] Fixed,

    /// <summary>Another worked-out value; the step's input traces it.</summary>
    [JsonStringEnumMemberName("calculated")] Calculated,
}

/// <summary>The outcome of a trace request.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TraceStatus>))]
public enum TraceStatus
{
    [JsonStringEnumMemberName("traced")] Traced,

    /// <summary>A value the server traces, but this character does not have it (no such spellcasting, no such statistic).</summary>
    [JsonStringEnumMemberName("missing")] Missing,

    /// <summary>A known value the server does not trace (the web app works it out); never answered with a made-up trace.</summary>
    [JsonStringEnumMemberName("unsupported")] Unsupported,
}

/// <summary>Where a step's value comes from: the element and rule for a rule, or the kind of input otherwise.</summary>
public sealed record TraceSource
{
    public required TraceSourceKind Kind { get; init; }

    /// <summary>The element the value comes from ("Ref Defensive Style"), or what the player entered ("Base score").</summary>
    public string? Name { get; init; }

    /// <summary>The element's type ("Class Feature", "Magic Item").</summary>
    public string? ElementType { get; init; }

    public Guid? ElementId { get; init; }
    public Guid? RegistrationId { get; init; }

    /// <summary>The element's statistic rule.</summary>
    public Guid? RuleId { get; init; }

    /// <summary>Aurora's bonus name of the rule: bonuses with the same one count once.</summary>
    public string? Bonus { get; init; }

    /// <summary>The statistic the rule's value refers to ("dexterity:modifier"), when it is not a number.</summary>
    public string? Refers { get; init; }
}

public sealed record TraceStep
{
    public required TraceOperation Operation { get; init; }

    /// <summary>The step's operand: what is added, the cap, the divisor; for a rounding step the value rounded.</summary>
    public required decimal Value { get; init; }

    /// <summary>The running value after this step.</summary>
    public required decimal Result { get; init; }

    public required string Label { get; init; }
    public TraceSource? Source { get; init; }

    /// <summary>How the operand itself was worked out, when it is a value of its own (a statistic a rule refers to).</summary>
    public CalculationTrace? Input { get; init; }
}

public sealed record TraceExclusion
{
    public required TraceExclusionReason Reason { get; init; }
    public required int Value { get; init; }
    public required string Label { get; init; }
    public TraceSource? Source { get; init; }

    /// <summary>What counted instead ("Ref Amulet of Warding +2, the same kind of bonus (warding)").</summary>
    public string? Detail { get; init; }
}

/// <summary>One value and how it was worked out. Replaying the steps from the first gives <see cref="Final"/>.</summary>
public sealed record CalculationTrace
{
    /// <summary>The value's key: "skill:athletics", "statistic:ac:misc", "armor-class".</summary>
    public required string Key { get; init; }

    public required string Label { get; init; }
    public required int Final { get; init; }
    public IReadOnlyList<TraceStep> Steps { get; init; } = [];
    public IReadOnlyList<TraceExclusion> Excluded { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// Which content the trace was worked out from: the sources and rules editions of the elements in it, and a
/// fingerprint of their rules (the same content gives the same fingerprint on any server).
/// </summary>
public sealed record CalculationTraceContent
{
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>"2014" and/or "2024": a source with "2024" in its name is the 2024 rules.</summary>
    public IReadOnlyList<string> Editions { get; init; } = [];

    public required string Fingerprint { get; init; }
}

public sealed record CalculationTraceResponse
{
    public int ContractVersion { get; init; } = CalculationTraceContract.Version;
    public required Guid CharacterId { get; init; }

    /// <summary>The key asked for.</summary>
    public required string Value { get; init; }

    public required TraceStatus Status { get; init; }

    /// <summary>The trace, when <see cref="Status"/> is traced.</summary>
    public CalculationTrace? Trace { get; init; }

    public CalculationTraceContent? Content { get; init; }

    /// <summary>Why the value is missing or unsupported.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// A refused trace request: a stable code ("invalid-argument", "unsupported-version", "not-found") and a message.
/// </summary>
public sealed record CalculationTraceError
{
    public int ContractVersion { get; init; } = CalculationTraceContract.Version;
    public required string Code { get; init; }
    public required string Message { get; init; }
}
