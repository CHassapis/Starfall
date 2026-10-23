using System.Xml.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Starlights.Modules.Elements.Data;
using Starlights.Modules.Elements.Domain;
using Starlights.Modules.Elements.Domain.Components;
using Starlights.Modules.Elements.Services;
using Starlights.Modules.Elements.Services.FiveETools;
using Starlights.Modules.Elements.Domain.Homebrew;
using Starlights.Platform.Data;

namespace Starlights.Modules.Elements.Tests;

/// <summary>The Homebrew page's spells, subclasses and classes as Aurora elements: round trips, and that they import.</summary>
[TestClass]
public class HomebrewWorkshopXmlTests
{
    public TestContext TestContext { get; set; } = default!;

    private static readonly HomebrewSpellModel Spell = new()
    {
        Name = "Moonlit Lash",
        Source = "Curse of Strahd (homebrew)",
        Level = 2,
        School = "Evocation",
        CastingTime = "1 bonus action",
        Range = "30 feet",
        Duration = "Concentration, up to 1 minute",
        Material = true,
        MaterialText = "a silver thread",
        Concentration = true,
        Lists = ["Wizard", "Moonblade"],
        Description = "A whip of moonlight strikes a creature.\n\nIt deals 2d8 radiant damage.",
        HigherLevels = "The damage increases by 1d8 for each slot level above 2.",
    };

    private static readonly HomebrewClassModel Moonblade = new()
    {
        Name = "Moonblade",
        Description = "A warrior of the silver moon.",
        HitDie = 10,
        SavingThrows = ["Strength", "Wisdom"],
        Armor = ["Light", "Medium", "Shields"],
        Weapons = ["Simple", "Martial"],
        MulticlassArmor = ["Light", "Shields"],
        MulticlassWeapons = ["Martial"],
        MulticlassAbility = "Wisdom",
        SkillChoices = 2,
        Skills = ["Athletics", "Insight", "Sleight of Hand"],
        SubclassLevel = 3,
        Features = [new() { Level = 1, Name = "Silver Strike", Description = "Your weapon sheds moonlight." }, new() { Level = 5, Name = "Extra Attack", Description = "You attack twice." }],
        Casting = "Half",
        CastingAbility = "Wisdom",
        SpellLists = ["Paladin"],
    };

    [TestMethod]
    public void ASpell_RoundTrips()
    {
        var xml = HomebrewWorkshopXml.SpellToXml(Spell, "ID_TEST_SPELL");
        xml.Element("supports")!.Value.Should().Be("Wizard, Moonblade");
        var back = HomebrewWorkshopXml.SpellFromXml(xml);
        back.Should().BeEquivalentTo(Spell with { Id = "ID_TEST_SPELL" });
    }

    [TestMethod]
    public void ASpellThatSaysNothingOfItsRoll_IsWrittenAsBefore()
    {
        var xml = HomebrewWorkshopXml.SpellToXml(Spell, "ID_TEST_SPELL");

        // the setters every spell had before mechanics could be said, and nothing else
        xml.Elements().Select(e => e.Name.LocalName).Should().Equal("supports", "description", "setters");
        xml.Element("setters")!.Elements().Select(s => (string?)s.Attribute("name")).Should().Equal(
            "level", "school", "time", "duration", "range", "hasVerbalComponent", "hasSomaticComponent", "hasMaterialComponent", "materialComponent", "isConcentration", "isRitual");
    }

    [TestMethod]
    public void ASpellsMechanics_AreSettersOfTheirOwn_AndRoundTrip()
    {
        var lance = Spell with { Name = "Frost Lance", Level = 0, HigherLevels = null, Roll = "ranged-attack", Damage = "1d10 cold", CantripScaling = "5:2d10; 11:3d10; 17:4d10" };
        var burst = Spell with { Name = "Searing Burst", Level = 3, Roll = "save", SaveAbility = "Dexterity", HalfOnSave = true, Damage = "8d6 fire; 2d6 radiant", AddsModifier = true, SlotScaling = "1d6" };

        MechanicsSetters(lance).Should().Equal(
            ("starlights-spell-roll", "ranged-attack"), ("starlights-spell-damage", "1d10 cold"), ("starlights-spell-cantrip-scaling", "5:2d10; 11:3d10; 17:4d10"));
        MechanicsSetters(burst).Should().Equal(
            ("starlights-spell-roll", "save"), ("starlights-spell-save", "Dexterity"), ("starlights-spell-save-success", "half"),
            ("starlights-spell-damage", "8d6 fire; 2d6 radiant"), ("starlights-spell-modifier", "true"), ("starlights-spell-slot-scaling", "1d6"));
        foreach (var spell in new[] { lance, burst })
        {
            HomebrewWorkshopXml.SpellFromXml(HomebrewWorkshopXml.SpellToXml(spell, "ID_TEST_SPELL")).Should().BeEquivalentTo(spell with { Id = "ID_TEST_SPELL" });
        }
    }

    private static List<(string, string)> MechanicsSetters(HomebrewSpellModel spell) =>
        HomebrewWorkshopXml.SpellToXml(spell, "ID_TEST_SPELL").Element("setters")!.Elements("set")
            .Select(s => ((string)s.Attribute("name")!, s.Value))
            .Where(s => s.Item1.StartsWith("starlights-spell-", StringComparison.Ordinal))
            .ToList();

    [TestMethod]
    public void ASubclass_RoundTrips_AndKeepsItsFeatureIdsWhenEdited()
    {
        var model = new HomebrewSubclassModel
        {
            Name = "Oath of the Mists",
            ClassSupports = "Paladin Subclass",
            ClassName = "Paladin (Player’s Handbook (2024))",
            Description = "Paladins sworn to Barovia's mists.",
            Features = [new() { Level = 3, Name = "Mist Step", Description = "You step through mist." }, new() { Level = 7, Name = "Aura of Fog", Description = "Fog surrounds you." }],
        };
        var root = new XElement("elements", HomebrewWorkshopXml.SubclassToXml(model, "ID_SUB"));
        var main = root.Elements("element").First();
        main.Attribute("type")!.Value.Should().Be("Archetype");
        main.Element("supports")!.Value.Should().Be("Paladin Subclass");
        root.Elements("element").Skip(1).Select(e => e.Attribute("name")!.Value).Should().Equal("Level 3: Mist Step", "Level 7: Aura of Fog");

        var back = HomebrewWorkshopXml.SubclassFromXml(root, main);
        back.Features.Select(f => (f.Level, f.Name)).Should().Equal((3, "Mist Step"), (7, "Aura of Fog"));
        back.ClassName.Should().Be(model.ClassName);

        // renaming a feature keeps its id, so characters keep it
        var renamed = back with { Features = [back.Features[0] with { Name = "Misty Step" }, back.Features[1]] };
        var again = HomebrewWorkshopXml.SubclassToXml(renamed, "ID_SUB");
        again.Skip(1).Select(e => e.Attribute("id")!.Value).Should().Equal(back.Features.Select(f => f.Id));
    }

    [TestMethod]
    public void AClass_RoundTrips()
    {
        var root = new XElement("elements", HomebrewWorkshopXml.ClassToXml(Moonblade, "ID_STARLIGHTS_HOMEBREW_CLASS_MOONBLADE"));
        var main = root.Elements("element").First();
        var back = HomebrewWorkshopXml.ClassFromXml(root, main);
        back.Should().BeEquivalentTo(Moonblade with
        {
            Id = "ID_STARLIGHTS_HOMEBREW_CLASS_MOONBLADE",
            Source = "Homebrew",
            SubclassSupports = "Moonblade Subclass",
            SpellListName = "Moonblade",
            Cantrips = 0, // half casters have none
            AsiLevels = [4, 8, 12, 16],
        }, o => o.Excluding(m => m.Features));
        back.Features.Select(f => (f.Level, f.Name)).Should().Equal((1, "Silver Strike"), (5, "Extra Attack"));
    }

    [TestMethod]
    public void AClass_FollowsTheBooksMulticlassPattern_AndItsSpellcastingTables()
    {
        var elements = HomebrewWorkshopXml.ClassToXml(Moonblade with { Casting = "Full", Cantrips = 3 }, "ID_STARLIGHTS_HOMEBREW_CLASS_MOONBLADE");
        var main = elements[0];
        var grants = main.Element("rules")!.Elements("grant").Where(g => g.Attribute("type")!.Value == "Proficiency").ToList();
        // what a multiclass character gets has no requirement; the rest needs "not multiclassed into it"
        grants.Single(g => g.Attribute("id")!.Value == "ID_PROFICIENCY_ARMOR_PROFICIENCY_LIGHT_ARMOR").Attribute("requirements").Should().BeNull();
        grants.Single(g => g.Attribute("id")!.Value == "ID_PROFICIENCY_ARMOR_PROFICIENCY_MEDIUM_ARMOR").Attribute("requirements")!.Value.Should().Be("!ID_STARLIGHTS_HOMEBREW_MULTICLASS_MOONBLADE");
        grants.Single(g => g.Attribute("id")!.Value == "ID_PROFICIENCY_SAVINGTHROW_WISDOM").Attribute("requirements").Should().NotBeNull();
        main.Element("rules")!.Elements("select").First().Attribute("supports")!.Value
            .Should().Be("ID_PROFICIENCY_SKILL_ATHLETICS|ID_PROFICIENCY_SKILL_INSIGHT|ID_PROFICIENCY_SKILL_SLEIGHTOFHAND");
        main.Element("multiclass")!.Element("requirements")!.Value.Should().Be("([wis:13],!ID_STARLIGHTS_HOMEBREW_CLASS_MOONBLADE)||ID_INTERNAL_GRANTS_MULTICLASS_UNLOCKER");

        var casting = elements.Single(e => e.Attribute("id")!.Value.EndsWith("_F_SPELLCASTING"));
        casting.Element("spellcasting")!.Element("list")!.Value.Should().Be("Moonblade, Paladin");
        var stats = casting.Element("rules")!.Elements("stat").ToList();
        // the 2024 Cleric's table: 4 first-level slots by level 3, 4 prepared at level 1
        stats.Where(s => s.Attribute("name")!.Value == "moonblade:spellcasting:slots:1").Sum(s => (int)s.Attribute("value")!).Should().Be(4);
        stats.Where(s => s.Attribute("name")!.Value == "moonblade:spellcasting:prepare").Sum(s => (int)s.Attribute("value")!).Should().Be(22);
        casting.Element("rules")!.Elements("grant").Select(g => g.Attribute("id")!.Value).Should().Contain("ID_INTERNAL_GRANT_MULTICLASS_SPELLCASTING_SLOTS_FULL");
        elements.Single(e => e.Attribute("id")!.Value.EndsWith("_F_SUBCLASS")).Descendants("select").Single().Attribute("supports")!.Value.Should().Be("Moonblade Subclass");
    }

    [TestMethod]
    public void SubclassTargets_AreReadFromTheContent()
    {
        var targets = HomebrewWorkshopXml.SubclassTargets(
        [
            ("Class", "ID_FIGHTER", "Fighter", "Player’s Handbook (2024)",
                """<element name="Fighter" type="Class" id="ID_FIGHTER"><rules><grant type="Class Feature" id="ID_FIGHTER_SUB" level="3" /></rules></element>"""),
            ("Class Feature", "ID_FIGHTER_SUB", "Level 3: Fighter Subclass", "Player’s Handbook (2024)",
                """<element name="Level 3: Fighter Subclass" type="Class Feature" id="ID_FIGHTER_SUB"><rules><select type="Archetype" name="Fighter Subclass" supports="Martial Archetype||Fighter Subclass" level="3" /></rules></element>"""),
            ("Class", "ID_NOSUB", "Sidekick", "Test", """<element name="Sidekick" type="Class" id="ID_NOSUB" />"""),
        ]);
        targets.Should().Equal(new SubclassTarget("Fighter", "Player’s Handbook (2024)", "Fighter Subclass"));
    }

    [TestMethod]
    public async Task TheGeneratedElements_Import()
    {
        var root = Directory.CreateTempSubdirectory("homebrew-workshop-tests").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content"));
            var homebrew = Directory.CreateDirectory(Path.Combine(root, "homebrew")).FullName;
            new XDocument(new XElement("elements", HomebrewWorkshopXml.SpellToXml(Spell, "ID_STARLIGHTS_HOMEBREW_SPELL_MOONLIT_LASH"))).Save(Path.Combine(homebrew, "starlights-spells.xml"));
            new XDocument(new XElement("elements", HomebrewWorkshopXml.ClassToXml(Moonblade with { Casting = "Pact", Cantrips = 2 }, "ID_STARLIGHTS_HOMEBREW_CLASS_MOONBLADE")))
                .Save(Path.Combine(homebrew, "starlights-classes.xml"));
            new XDocument(new XElement("elements", HomebrewWorkshopXml.SubclassToXml(new HomebrewSubclassModel
            {
                Name = "Circle of Ash",
                ClassSupports = "Moonblade Subclass",
                ClassName = "Moonblade",
                Features = [new() { Level = 3, Name = "Ashen Veil" }],
            }, "ID_STARLIGHTS_HOMEBREW_SUBCLASS_CIRCLE_OF_ASH"))).Save(Path.Combine(homebrew, "starlights-subclasses.xml"));

            var added = new List<Element>();
            var repository = new Mock<IElementsRepository>();
            repository.Setup(x => x.GetElementSummariesAsync()).ReturnsAsync([]);
            repository.Setup(x => x.Add(It.IsAny<Element>())).Callback<Element>(added.Add);
            var persistence = new Mock<IPersistence>();
            persistence.Setup(x => x.GetRepository<IElementsRepository>()).Returns(repository.Object);
            var importer = new AuroraImporter(NullLogger<AuroraImporter>.Instance, persistence.Object,
                new AuroraImporterOptions(Path.Combine(root, "content"), HomebrewPath: homebrew),
                new FiveEToolsData(new FiveEToolsOptions(Path.Combine(root, "no-5etools")), NullLogger<FiveEToolsData>.Instance));
            await importer.ImportAsync(AuroraImporterOptions.HomebrewIndex, cancellationToken: TestContext.CancellationToken);

            var byId = added.ToDictionary(e => e.GetRequiredComponent<AuroraSourceComponent>().AuroraId);
            byId["ID_STARLIGHTS_HOMEBREW_SPELL_MOONLIT_LASH"].Type.Should().Be("Spell");
            byId["ID_STARLIGHTS_HOMEBREW_CLASS_MOONBLADE"].Type.Should().Be("Class");
            byId["ID_STARLIGHTS_HOMEBREW_CLASS_MOONBLADE_F_SPELLCASTING"].Type.Should().Be("Class Feature");
            byId["ID_STARLIGHTS_HOMEBREW_SUBCLASS_CIRCLE_OF_ASH"].Type.Should().Be(ElementTypeConstants.SubClass); // the importer's name for an Archetype
            byId["ID_STARLIGHTS_HOMEBREW_SUBCLASS_CIRCLE_OF_ASH_F_ASHEN_VEIL"].Type.Should().Be("Archetype Feature");
            byId.Keys.Should().Contain(k => k.Contains("MULTICLASS_MOONBLADE"), "the importer makes the class's multiclass entry");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
