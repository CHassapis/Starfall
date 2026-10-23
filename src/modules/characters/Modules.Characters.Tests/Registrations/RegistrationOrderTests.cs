using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Starlights.Modules.Characters.Data;
using Starlights.Modules.Characters.Domain.Abilities;
using Starlights.Modules.Characters.Domain.Characters;
using Starlights.Modules.Characters.Domain.Elements;
using Starlights.Modules.Characters.Domain.Registrations;
using Starlights.Modules.Characters.Domain.SavingThrows;
using Starlights.Modules.Characters.Domain.Services;
using Starlights.Modules.Characters.Domain.Skills;
using Starlights.Modules.Characters.Services.Processing;
using Starlights.Modules.Characters.Services.Processing.Behaviors;
using Starlights.Modules.Characters.Services.Statistics;
using Starlights.Modules.Characters.Services.Statistics.Processors;
using Starlights.Modules.Elements.Integration;
using Starlights.Modules.Elements.Integration.Models;
using Starlights.Platform.Data;

namespace Starlights.Modules.Characters.Tests.Registrations;

/// <summary>
/// SQL Server returns a character's registrations in no particular order, so a new character's "Skills" rule could be
/// handled before its "Abilities" rule: every skill was registered while no ability score existed, got no skill, and
/// the character had no skills on its sheet at all (6 of 14 Aurora imports on a disposable install).
/// </summary>
[TestClass]
public sealed class RegistrationOrderTests
{
    public TestContext TestContext { get; set; } = default!;

    private readonly Mock<IPersistence> _persistence = new();
    private readonly Mock<IElementsModuleQueries> _elements = new();
    private readonly Mock<IRegistrationRepository> _registrations = new();
    private readonly Mock<ICharactersRepository> _characters = new();
    private readonly Character _character = new CharacterCreationService().Create("Fedra");
    private readonly Guid _dexterity = Guid.NewGuid();
    private readonly List<Registration> _abilityRegistrations = [];

    [TestInitialize]
    public void Setup()
    {
        _persistence.Setup(p => p.GetRepository<IRegistrationRepository>()).Returns(_registrations.Object);
        _persistence.Setup(p => p.GetRepository<ICharactersRepository>()).Returns(_characters.Object);
        _persistence.Setup(p => p.SaveChangesAsync()).ReturnsAsync(1);
        _characters.Setup(c => c.GetCharacterAsync(It.IsAny<Guid>())).ReturnsAsync(_character);
        _registrations.Setup(r => r.GetRegistrationsByAssociationsAsync(_character.Id, new ElementId(_dexterity)))
                      .ReturnsAsync(() => _abilityRegistrations.ToList());
    }

    private void RegisterDexterity()
    {
        var dexterity = Registration.Create(_character.Id, new(_dexterity), "Dexterity", "Ability");
        _abilityRegistrations.Add(dexterity);
        _character.GetRequiredComponent<AbilitiesComponent>().CreateAbilityScore(dexterity.Id, "Dexterity", "DEX", 2);
    }

    [TestMethod]
    public async Task SkillRegisteredBeforeItsAbilityScore_GetsItsSkillOnRepair_Once()
    {
        var acrobatics = Guid.NewGuid();
        _elements.Setup(e => e.GetSkillModel(acrobatics)).ReturnsAsync(new SkillDataModel(acrobatics, "Acrobatics", _dexterity));
        var behavior = new SkillRegistrationBehavior(NullLogger<SkillRegistrationBehavior>.Instance, _persistence.Object, _elements.Object);
        var registration = Registration.Create(_character.Id, new(acrobatics), "Acrobatics", "Skill");
        var skills = _character.GetRequiredComponent<SkillsComponent>();

        await behavior.Registered(registration);
        skills.Skills.Should().BeEmpty("no ability score is registered yet");

        RegisterDexterity();
        await behavior.Repair(_character, registration);
        await behavior.Repair(_character, registration);
        await behavior.Registered(registration);

        skills.Skills.Should().ContainSingle().Which.Should().Match<Skill>(s => s.Name == "Acrobatics" && s.AbilityScoreAbbreviation == "DEX" && s.AssociatedRegistrationId == registration.Id);
    }

    [TestMethod]
    public async Task SavingThrowRegisteredBeforeItsAbilityScore_GetsItOnRepair_Once()
    {
        var save = Guid.NewGuid();
        _elements.Setup(e => e.GetSavingThrowModel(save)).ReturnsAsync(new SavingThrowDataModel(save, "Dexterity Saving Throw", _dexterity));
        var behavior = new SavingThrowRegistrationBehavior(NullLogger<SavingThrowRegistrationBehavior>.Instance, _persistence.Object, _elements.Object);
        var registration = Registration.Create(_character.Id, new(save), "Dexterity Saving Throw", "Saving Throw");
        var saves = _character.GetRequiredComponent<SavingThrowsComponent>();

        await behavior.Registered(registration);
        saves.SavingThrows.Should().BeEmpty();

        RegisterDexterity();
        await behavior.Repair(_character, registration);
        await behavior.Repair(_character, registration);

        saves.SavingThrows.Should().ContainSingle().Which.AbilityScoreAbbreviation.Should().Be("DEX");
    }

    [TestMethod]
    public async Task Reprocessing_HandlesRegistrationsInTheOrderTheyWereMade_ThenRepairs()
    {
        var characterId = CharacterId.New();
        var abilities = Registration.Create(characterId, new(Guid.NewGuid()), "Abilities", "Rule");
        await Task.Delay(5, TestContext.CancellationToken); // version 7 ids order by the millisecond
        var skills = Registration.Create(characterId, new(Guid.NewGuid()), "Skills", "Rule");
        _registrations.Setup(r => r.GetRegistrationsAsync(characterId)).ReturnsAsync(() => new List<Registration> { skills, abilities });
        var handled = new List<Guid>();
        _elements.Setup(e => e.GetElementWithRules(It.IsAny<Guid>()))
                 .Callback<Guid>(handled.Add)
                 .ReturnsAsync((Guid id) => new ElementDataModel { Id = id, Name = "Rule", Type = "Rule", IncludeRules = [] });
        var repairs = new RecordingBehavior();
        var manager = new RegistrationManager(Mock.Of<ILogger<RegistrationManager>>(), _persistence.Object, [repairs]);
        var statistics = new StatisticsCalculator(NullLogger<StatisticsCalculator>.Instance, [], [new ProficiencyGroupProcessor(NullLogger<ProficiencyGroupProcessor>.Instance)], []);
        var processor = new RegistrationProcessor(Mock.Of<ILogger<RegistrationProcessor>>(), _persistence.Object, manager, _elements.Object, statistics);

        await processor.ReproccessRegistrations(characterId);

        handled.Should().Equal(abilities.AssociatedElementId.Value, skills.AssociatedElementId.Value);
        repairs.Repaired.Should().BeEquivalentTo([abilities.Id, skills.Id]);
        repairs.Character.Should().BeSameAs(_character);
    }

    private sealed class RecordingBehavior : IRegistrationBehavior
    {
        public List<RegistrationId> Repaired { get; } = [];
        public Character? Character { get; private set; }

        public Task Registered(Registration newRegistration) => Task.CompletedTask;

        public Task Unregister(Registration existingRegistration) => Task.CompletedTask;

        public Task Repair(Character character, Registration existingRegistration)
        {
            Character = character;
            Repaired.Add(existingRegistration.Id);
            return Task.CompletedTask;
        }
    }
}
