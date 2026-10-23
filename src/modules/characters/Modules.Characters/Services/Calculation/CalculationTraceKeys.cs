using System.Text.RegularExpressions;

namespace Starlights.Modules.Characters.Services.Calculation;

/// <summary>
/// The values a trace can be asked for. The server traces what it calculates:
/// <list type="bullet">
/// <item><c>ability-score:&lt;ability&gt;</c>, <c>ability-modifier:&lt;ability&gt;</c>, <c>saving-throw:&lt;ability&gt;</c> (strength … charisma)</item>
/// <item><c>proficiency-bonus</c>, <c>skill:&lt;skill&gt;</c> (athletics, sleight-of-hand, …)</item>
/// <item><c>spell-attack:&lt;spellcasting&gt;</c>, <c>spell-save-dc:&lt;spellcasting&gt;</c> (the spellcasting's name as a slug: wizard, ref-bard)</item>
/// <item><c>statistic:&lt;name&gt;</c>: any statistic (ac:misc, innate-speed, ranged:attack)</item>
/// </list>
/// The web app traces what it works out itself, in the same shape: <c>armor-class</c>, <c>hit-points</c>,
/// <c>initiative</c>, <c>passive-perception</c>, <c>speed:&lt;walk|fly|climb|swim&gt;</c>,
/// <c>attack:&lt;inventory or custom attack id&gt;:&lt;to-hit|damage&gt;</c>.
/// </summary>
public static partial class CalculationTraceKeys
{
    public static readonly IReadOnlyList<string> Abilities = ["strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma"];

    [GeneratedRegex(@"^(ability-score|ability-modifier|saving-throw):(strength|dexterity|constitution|intelligence|wisdom|charisma)$|^proficiency-bonus$|^skill:[a-z][a-z-]{0,63}$|^(spell-attack|spell-save-dc):[a-z0-9][a-z0-9'()-]{0,99}$|^statistic:[a-z0-9-][a-z0-9:'()+_-]{0,199}$")]
    private static partial Regex ServerKey();

    [GeneratedRegex(@"^(armor-class|hit-points|initiative|passive-perception)$|^speed:(walk|fly|climb|swim)$|^attack:[A-Za-z0-9_-]{1,64}:(to-hit|damage)$")]
    private static partial Regex WebKey();

    /// <summary>A value the server traces.</summary>
    public static bool IsServerKey(string key) => ServerKey().IsMatch(key);

    /// <summary>A value the web app works out and traces itself.</summary>
    public static bool IsWebKey(string key) => WebKey().IsMatch(key);
}
