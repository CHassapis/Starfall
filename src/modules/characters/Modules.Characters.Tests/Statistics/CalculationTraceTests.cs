using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Starlights.Modules.Characters.Domain.Abilities;
using Starlights.Modules.Characters.Domain.Characters;
using Starlights.Modules.Characters.Domain.Elements;
using Starlights.Modules.Characters.Domain.Progression;
using Starlights.Modules.Characters.Domain.Registrations;
using Starlights.Modules.Characters.Domain.SavingThrows;
using Starlights.Modules.Characters.Domain.Skills;
using Starlights.Modules.Characters.Services.Calculation;
using Starlights.Modules.Characters.Services.Statistics;
using Starlights.Modules.Characters.Services.Statistics.Initializers;
using Starlights.Modules.Characters.Services.Statistics.Processors;

namespace Starlights.Modules.Characters.Tests.Statistics;

/// <summary>
/// Calculation traces: a traced calculation gives the same figures as an untraced one, the order the registrations
/// come in does not change any figure or label, and each trace's steps come to the figure it explains.
/// </summary>
[TestClass]
public sealed class CalculationTraceTests
{
    // the calculator as CharactersModule registers it (initializers, group processors and post-processors in that order)
    private static StatisticsCalculator Calculator() => new(NullLogger<StatisticsCalculator>.Instance,
        [new CharacterStatisticsInitializer(), new AbilitiesStatisticsInitializer(), new SavingThrowStatisticsInitializer(), new SkillStatisticsInitializer()],
        [new AbilitiesGroupProcessor(), new ProficiencyGroupProcessor(NullLogger<ProficiencyGroupProcessor>.Instance)],
        [new SkillStatisticsPostProcessor(), new SavingThrowStatisticsPostProcessor()]);

    /// <summary>
    /// A level 5 character with a bit of everything: stacking proficiency bonuses, a skill proficiency from two
    /// places, half proficiency, an item that sets Strength, two unarmored defenses that tie, bonuses of the same
    /// kind, and a statistic that refers to one nothing gives.
    /// </summary>
    private static (Character Character, List<Registration> Registrations) Build(int strength = 15, int setStrength = 19)
    {
        var character = Character.Create("Trace Test");
        var progression = ProgressionComponent.Create(character.Id);
        progression.SetCharacterLevel(5);
        character.AddComponent(progression);
        var abilities = AbilitiesComponent.Create(character.Id);
        character.AddComponent(abilities);
        var skills = SkillsComponent.Create(character.Id);
        character.AddComponent(skills);
        var saves = SavingThrowsComponent.Create(character.Id);
        character.AddComponent(saves);

        AbilityScore Ability(string name, string abbreviation, int score)
        {
            var a = abilities.CreateAbilityScore(Registration.Create(character.Id, new ElementId(Guid.NewGuid()), name, "Ability").Id, name, abbreviation, 0);
            a.UpdateBaseScore(score);
            return a;
        }
        var str = Ability("Strength", "STR", strength);
        var dex = Ability("Dexterity", "DEX", 14);
        var wis = Ability("Wisdom", "WIS", 12);
        skills.CreateSkill(Registration.Create(character.Id, new ElementId(Guid.NewGuid()), "Athletics", "Skill").Id, "Athletics", str.Id, "STR");
        skills.CreateSkill(Registration.Create(character.Id, new ElementId(Guid.NewGuid()), "Stealth", "Skill").Id, "Stealth", dex.Id, "DEX");
        skills.CreateSkill(Registration.Create(character.Id, new ElementId(Guid.NewGuid()), "Perception", "Skill").Id, "Perception", wis.Id, "WIS");
        saves.CreateSavingThrow(Registration.Create(character.Id, new ElementId(Guid.NewGuid()), "Strength Saving Throw", "Saving Throw").Id, "Strength Saving Throw", str.Id, "STR");
        saves.CreateSavingThrow(Registration.Create(character.Id, new ElementId(Guid.NewGuid()), "Wisdom Saving Throw", "Saving Throw").Id, "Wisdom Saving Throw", wis.Id, "WIS");

        var registrations = new List<Registration>();
        Registration Element(string name, string type, params (string Statistic, string Value, string? Bonus)[] rules)
        {
            // ids are time-ordered to the millisecond: a millisecond apart, each is made after the one before
            Thread.Sleep(2);
            var registration = Registration.Create(character.Id, new ElementId(Guid.NewGuid()), name, type);
            foreach (var (statistic, value, bonus) in rules)
            {
                registration.CreateStatisticRule(new ElementComponentId(Guid.NewGuid()), statistic, value).UpdateStackingBonus(bonus);
            }
            registrations.Add(registration);
            return registration;
        }
        Element("Proficiency Bonus", "Internal", ("proficiency", "2", "base"), ("proficiency", "3", "base"));
        Element("Strength", "Ability", ("strength:max", "20", "base"));
        Element("Species", "Species", ("strength", "2", null), ("innate-speed", "30", "base"));
        Element("Athletics Proficiency", "Proficiency", ("athletics:proficiency", "proficiency", "proficiency"), ("strength-saving-throw:proficiency", "proficiency", "proficiency"));
        Element("Rogue Stealth", "Proficiency", ("stealth:proficiency", "proficiency", "proficiency"));
        Element("Urchin Stealth", "Proficiency", ("stealth:proficiency", "proficiency", "proficiency"));
        Element("Versatility", "Class Feature", ("perception:misc", "proficiency:half", null));
        Element("Gauntlets", "Magic Item", ("strength:score:set", setStrength.ToString(), null));
        Element("Monk Defense", "Class Feature", ("ac:monk", "10", "base"), ("ac:monk", "dexterity:modifier", "primary"), ("ac:calculation", "ac:monk", "calculation"));
        Element("Stone Hide", "Species Feature", ("ac:stone", "12", "base"), ("ac:calculation", "ac:stone", "calculation"));
        Element("Ring of Warding", "Magic Item", ("ac:misc", "1", "warding"));
        Element("Amulet of Warding", "Magic Item", ("ac:misc", "2", "warding"));
        Element("Cloak", "Magic Item", ("ac:misc", "1", null));
        Element("Familiar", "Class Feature", ("companion:perception", "companion:proficiency", null));
        return (character, registrations);
    }

    /// <summary>Everything a calculation produces: each statistic with its values in order, and what it changed on the character.</summary>
    private static string Snapshot(Character character, StatisticsCalculationResult result) => JsonSerializer.Serialize(new
    {
        statistics = result.Statistics.OrderBy(g => g.GroupName, StringComparer.Ordinal)
            .Select(g => new { g.GroupName, g.IsCompleted, Sum = g.Sum(), Values = g.GetStatisticValues().Select(v => $"{v.Source}|{v.Value}|{v.DisplayName}|{v.RuleId}") }),
        abilities = character.GetRequiredComponent<AbilitiesComponent>().AbilityScores.Select(a => $"{a.Name} {a.AdditionalScore} {a.CalculatedScore} {a.CalculatedModifier}"),
        skills = character.GetRequiredComponent<SkillsComponent>().Skills.Select(s => $"{s.Name} {s.AbilityScoreModifier} {s.AdditionalBonus} {s.CalculatedBonus}"),
        saves = character.GetRequiredComponent<SavingThrowsComponent>().SavingThrows.Select(s => $"{s.Name} {s.AbilityScoreModifier} {s.AdditionalBonus} {s.CalculatedBonus}"),
        result.Errors,
    });

    [TestMethod]
    public void ATracedCalculation_GivesTheSameFiguresAsAnUntracedOne()
    {
        var (character, registrations) = Build();
        var calculator = Calculator();

        var untraced = Snapshot(character, calculator.Calculate(character, registrations));
        var traced = Snapshot(character, calculator.Calculate(character, registrations, new StatisticsTraceRecorder()));
        var again = Snapshot(character, calculator.Calculate(character, registrations));

        traced.Should().Be(untraced);
        again.Should().Be(untraced);
    }

    [TestMethod]
    public void TheOrderOfTheRegistrations_ChangesNoFigureAndNoLabel()
    {
        var (character, registrations) = Build();
        var calculator = Calculator();
        var expected = Snapshot(character, calculator.Calculate(character, registrations));

        var random = new Random(20261022);
        for (var i = 0; i < 12; i++)
        {
            var shuffled = registrations.OrderBy(_ => random.Next()).ToList();
            Snapshot(character, calculator.Calculate(character, shuffled)).Should().Be(expected);
            Snapshot(character, calculator.Calculate(character, shuffled, new StatisticsTraceRecorder())).Should().Be(expected);
        }
    }

    [TestMethod]
    public void TwoEqualBonusesOfTheSameKind_TheEarlierRegistrationCounts_InWhateverOrderTheyAreLoaded()
    {
        // Monk Defense (10 + Dex 2) and Stone Hide (12) tie for ac:calculation; the database returns rows in no set
        // order, and the sheet names the armor class after the one that counted
        var (character, registrations) = Build();
        var calculator = Calculator();

        foreach (var order in new[] { registrations, Enumerable.Reverse(registrations).ToList() })
        {
            var group = calculator.Calculate(character, order).Statistics.GetGroup("ac:calculation");
            group.Sum().Should().Be(12);
            group.GetStatisticValues().Should().ContainSingle().Which.Source.Should().Be("Monk Defense");
        }
    }

    [TestMethod]
    public void EveryTrace_ComesToItsFigure_AndTracingTwiceGivesTheSameTrace()
    {
        var (character, registrations) = Build();
        var recorder = new StatisticsTraceRecorder();
        var result = Calculator().Calculate(character, registrations, recorder);
        var builder = new CalculationTraceBuilder(recorder, result.Statistics, registrations);

        var keys = new List<string> { "proficiency-bonus", "skill:athletics", "skill:stealth", "skill:perception", "saving-throw:strength", "saving-throw:wisdom" };
        keys.AddRange(new[] { "strength", "dexterity", "wisdom" }.SelectMany(a => new[] { $"ability-score:{a}", $"ability-modifier:{a}" }));
        keys.AddRange(result.Statistics.Where(g => g.HasStatisticValues()).Select(g => $"statistic:{g.GroupName}"));

        foreach (var key in keys)
        {
            var (status, trace, reason) = builder.Build(key);
            status.Should().Be(TraceStatus.Traced, $"{key}: {reason}");
            AllOf(trace!).Should().AllSatisfy(t =>
            {
                t.Notes.Should().BeEmpty($"{key} / {t.Key}");
                CalculationTraceBuilder.Replay(t.Steps).Should().Be(t.Final, $"{key} / {t.Key}");
            });
            JsonSerializer.Serialize(builder.Build(key).Trace).Should().Be(JsonSerializer.Serialize(trace), key);
        }

        // the figures themselves, from the calculation
        builder.Build("skill:athletics").Trace!.Final.Should().Be(character.GetRequiredComponent<SkillsComponent>().Skills.Single(s => s.Name == "Athletics").CalculatedBonus);
        builder.Build("ability-score:strength").Trace!.Final.Should().Be(19);
        builder.Build("statistic:ac:misc").Trace!.Final.Should().Be(3);
    }

    [TestMethod]
    public void BonusesOfTheSameKind_TheLowerOnesAreExcludedAsStacking()
    {
        var (character, registrations) = Build();
        var builder = Traced(character, registrations);

        var proficiency = builder.Build("proficiency-bonus").Trace!;
        proficiency.Final.Should().Be(3);
        proficiency.Excluded.Should().ContainSingle().Which.Should().Match<TraceExclusion>(e => e.Reason == TraceExclusionReason.Stacking && e.Value == 2);

        var misc = builder.Build("statistic:ac:misc").Trace!;
        misc.Steps.Select(s => (s.Label, s.Value)).Should().Equal(("Cloak", 1m), ("Amulet of Warding", 2m));
        misc.Excluded.Should().ContainSingle().Which.Should().Match<TraceExclusion>(e => e.Reason == TraceExclusionReason.Stacking && e.Value == 1 && e.Label == "Ring of Warding");

        // a skill proficiency from two places counts once
        var stealth = builder.Build("skill:stealth").Trace!;
        stealth.Final.Should().Be(2 + 3);
        stealth.Steps[1].Input!.Excluded.Should().ContainSingle().Which.Reason.Should().Be(TraceExclusionReason.Stacking);
    }

    [TestMethod]
    public void AnAbilityScore_ShowsTheBase_TheIncreases_TheCap_AndASetValue()
    {
        // 15 + 2, Gauntlets set 19: replaced
        var trace = Traced(Build()).Build("ability-score:strength").Trace!;
        trace.Steps.Select(s => s.Operation).Should().Equal(TraceOperation.Start, TraceOperation.Add, TraceOperation.Replace);
        trace.Steps[0].Source!.Kind.Should().Be(TraceSourceKind.UserSupplied);
        trace.Steps[2].Source!.Name.Should().Be("Gauntlets");
        trace.Final.Should().Be(19);

        // 19 + 2 = 21, capped at 20; the Gauntlets' 19 is not higher
        var capped = Traced(Build(strength: 19)).Build("ability-score:strength").Trace!;
        capped.Steps.Select(s => s.Operation).Should().Equal(TraceOperation.Start, TraceOperation.Add, TraceOperation.Minimum);
        capped.Final.Should().Be(20);
        capped.Excluded.Should().ContainSingle().Which.Should().Match<TraceExclusion>(e => e.Reason == TraceExclusionReason.SetNotHigher && e.Value == 19);
    }

    [TestMethod]
    public void HalfProficiency_IsDividedAndRoundedDown_AndAModifierToo()
    {
        var builder = Traced(Build());

        // Perception: Wis +1, and half of proficiency 3 = 1.5, rounded down to 1
        var perception = builder.Build("skill:perception").Trace!;
        perception.Final.Should().Be(2);
        var half = perception.Steps.Single(s => s.Label == "Other bonuses").Input!.Steps.Single().Input!;
        half.Steps.Select(s => (s.Operation, s.Result)).Should().Equal((TraceOperation.Start, 3m), (TraceOperation.Divide, 1.5m), (TraceOperation.RoundDown, 1m));

        var modifier = builder.Build("ability-modifier:dexterity").Trace!;
        modifier.Steps.Select(s => s.Operation).Should().Equal(TraceOperation.Start, TraceOperation.Add, TraceOperation.Divide, TraceOperation.RoundDown);
        modifier.Final.Should().Be(2);
    }

    [TestMethod]
    public void WhatTheServerDoesNotTraceOrTheCharacterDoesNotHave_IsSaidSo_NeverMadeUp()
    {
        var builder = Traced(Build());

        builder.Build("statistic:companion:perception").Should().Match<(TraceStatus Status, CalculationTrace? Trace, string? Reason)>(r =>
            r.Status == TraceStatus.Missing && r.Trace == null && r.Reason!.Contains("companion:proficiency"));
        builder.Build("statistic:flying-speed").Status.Should().Be(TraceStatus.Missing);
        builder.Build("skill:arcana").Status.Should().Be(TraceStatus.Missing);
        builder.Build("spell-attack:wizard").Status.Should().Be(TraceStatus.Missing);
        builder.Build("armor-class").Status.Should().Be(TraceStatus.Unsupported);
        builder.Build("attack:abc:to-hit").Status.Should().Be(TraceStatus.Unsupported);
        builder.Build("not a key").Status.Should().Be(TraceStatus.Unsupported);
    }

    [TestMethod]
    public void TraceKeys_AreTheServersOrTheWebApps()
    {
        CalculationTraceKeys.IsServerKey("skill:sleight-of-hand").Should().BeTrue();
        CalculationTraceKeys.IsServerKey("statistic:ac:misc").Should().BeTrue();
        CalculationTraceKeys.IsServerKey("spell-save-dc:ref-bard").Should().BeTrue();
        CalculationTraceKeys.IsServerKey("ability-score:luck").Should().BeFalse();
        CalculationTraceKeys.IsServerKey("statistic:").Should().BeFalse();
        CalculationTraceKeys.IsWebKey("speed:walk").Should().BeTrue();
        CalculationTraceKeys.IsWebKey("attack:4b0e7a10-5c1d-4f2e-9a3b-000000000c10:to-hit").Should().BeTrue();
        CalculationTraceKeys.IsWebKey("attack:x:range").Should().BeFalse();
    }

    private static CalculationTraceBuilder Traced((Character Character, List<Registration> Registrations) build) => Traced(build.Character, build.Registrations);

    private static CalculationTraceBuilder Traced(Character character, List<Registration> registrations)
    {
        var recorder = new StatisticsTraceRecorder();
        var result = Calculator().Calculate(character, registrations, recorder);
        return new CalculationTraceBuilder(recorder, result.Statistics, registrations);
    }

    private static IEnumerable<CalculationTrace> AllOf(CalculationTrace trace) =>
        new[] { trace }.Concat(trace.Steps.Where(s => s.Input is not null).SelectMany(s => AllOf(s.Input!)));
}
