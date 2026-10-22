using Starlights.Modules.Characters.Domain.Abilities;
using Starlights.Modules.Characters.Domain.Skills;
using Starlights.Modules.Characters.Services.Calculation;

namespace Starlights.Modules.Characters.Services.Statistics.Processors;

internal sealed class SkillStatisticsPostProcessor : IStatisticsPostProcessor
{
    public int Order => 15; // after proficiency and abilities

    public void Process(StatisticsProcessorContext context)
    {
        var skillsComponent = context.Character.GetRequiredComponent<SkillsComponent>();

        // the ability modifier in the same pass as the bonuses (see the saving throw post-processor)
        foreach (var ability in context.Character.GetRequiredComponent<AbilitiesComponent>().AbilityScores)
        {
            skillsComponent.UpdateAbilityScoreModifier(ability.Id, ability.CalculatedModifier);
        }

        foreach (var skill in skillsComponent.Skills)
        {
            var slug = skill.Name.ToSlug();

            var proficiencyGroup = context.Statistics.GetGroup($"{slug}:proficiency");
            var miscGroup = context.Statistics.GetGroup($"{slug}:misc");

            var additionalBonus = proficiencyGroup.Sum() + miscGroup.Sum();

            skill.UpdateAdditionalBonus(additionalBonus);
            context.Trace?.Check($"skill:{slug}", new RecordedCheck(skill.Name, skill.AbilityScoreAbbreviation, skill.AbilityScoreModifier,
                proficiencyGroup.GroupName, proficiencyGroup.Sum(), miscGroup.GroupName, miscGroup.Sum(), skill.CalculatedBonus));

            // TODO: passive calculation
            //var passiveGroup = context.Statistics.GetGroup($"{slug}:passive");
        }
    }
}