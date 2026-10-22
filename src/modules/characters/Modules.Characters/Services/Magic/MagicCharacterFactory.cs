using Starlights.Modules.Characters.Domain.Abilities;
using Starlights.Modules.Characters.Domain.Characters;
using Starlights.Modules.Characters.Domain.Classes;
using Starlights.Modules.Characters.Domain.Registrations;
using Starlights.Modules.Characters.Services.Statistics;

namespace Starlights.Modules.Characters.Services.Magic;

/// <summary>
/// The inputs of <see cref="SpellcastingFacts.Build"/> from a character, its registrations and the statistics just
/// calculated for it (the spellcasting endpoint and the calculation trace use the same).
/// </summary>
public static class MagicCharacterFactory
{
    public static MagicCharacter Create(Character character, IReadOnlyCollection<Registration> registrations, StatisticValuesGroupCollection statistics)
    {
        int? Statistic(string name) => statistics.TryGetGroup(name, out var group) && group.IsCompleted ? group.Sum() : null;
        var abilities = character.GetRequiredComponent<AbilitiesComponent>().AbilityScores;
        var classes = character.GetRequiredComponent<ClassComponent>();

        return new MagicCharacter(
            registrations.Select(r => new MagicRegistration(
                r.Id.Value,
                r.ParentRegistrationId?.Value,
                r.AssociatedElementId.Value,
                r.AssociatedElementType,
                r.AssociatedElementName,
                r.SelectionRules.Where(s => s.SelectedOption is not null).Select(s => new MagicSelection(s.Name, s.SelectedOption!.Value)).ToList())).ToList(),
            classes.Classes.Select(c => new MagicClass(c.Registration.Value, c.Name, c.Level)).ToList(),
            Math.Max(1, classes.CalculateCharacterLevel()),
            Statistic("proficiency") ?? 2,
            ability => abilities.FirstOrDefault(a => string.Equals(a.Name, ability, StringComparison.OrdinalIgnoreCase))?.CalculatedModifier ?? 0,
            Statistic,
            character.RestrictedSources.ToList());
    }
}
