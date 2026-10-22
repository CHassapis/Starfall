using System.Text.Json;
using AwesomeAssertions;
using Starlights.Modules.Characters.Domain.Characters;

namespace Starlights.Modules.Characters.Tests.Combat;

[TestClass]
public class CharacterCombatTests
{
    [TestMethod]
    public void A_fight_in_progress_is_valid()
    {
        new CharacterCombat
        {
            Damage = 12,
            TemporaryHitPoints = 5,
            HitDiceSpent = 2,
            Uses = new() { ["Second Wind"] = 1, ["Action Surge"] = 1 },
            Concentration = "Bless",
            Conditions = ["Prone", "Frightened"],
            Exhaustion = 1,
            DeathSaveSuccesses = 2,
            DeathSaveFailures = 1,
            Companions = [new CharacterCompanion { Id = "f1", Name = "Owl", Key = "owl_xmm", MaxHitPoints = 1, Damage = 1 }],
        }.Validate().Should().BeNull();
    }

    [TestMethod]
    public void Impossible_values_are_refused()
    {
        new CharacterCombat { Damage = -1 }.Validate().Should().NotBeNull();
        new CharacterCombat { Uses = new() { [""] = 1 } }.Validate().Should().NotBeNull();
        new CharacterCombat { Conditions = [new string('x', 61)] }.Validate().Should().NotBeNull();
        new CharacterCombat { DeathSaveFailures = 4 }.Validate().Should().NotBeNull();
        new CharacterCombat { Exhaustion = 11 }.Validate().Should().NotBeNull();
        new CharacterCombat { Companions = [new CharacterCompanion { Id = "a", Name = "" }] }.Validate().Should().NotBeNull();
        new CharacterCombat { Companions = [new CharacterCompanion { Id = "a", Name = "Owl", Damage = -1 }] }.Validate().Should().NotBeNull();
    }

    private static CharacterResource Resource(string id = "r1") => new() { Id = id, Name = "Test Pool", Max = new() { From = "class-level", ClassName = "Paladin", Times = 5 }, Recharge = "dawn" };

    [TestMethod]
    public void Resources_of_the_players_own_are_valid()
    {
        new CharacterCombat
        {
            Uses = new() { ["custom:r1"] = 3 },
            Effects = ["custom:r3"],
            Resources =
            [
                Resource(),
                new() { Id = "r2", Name = "Test Touch", Pool = "Test Pool", Cost = 5 },
                new() { Id = "r3", Name = "Test Stance", Max = new() { From = "ability", Ability = "CHA", AtLeast = 1 }, Effect = new() { ArmorClass = 1, Speed = 10, Damage = "1d6 fire" } },
            ],
        }.Validate().Should().BeNull();
        new CharacterCombat { Resources = null }.Validate().Should().BeNull();
    }

    [TestMethod]
    public void Resources_out_of_bounds_are_refused()
    {
        CharacterResource[] refused =
        [
            Resource() with { Id = "" },
            Resource() with { Id = "a b" },
            Resource() with { Id = new string('x', 65) },
            Resource() with { Name = " " },
            Resource() with { Name = new string('x', 61) },
            Resource() with { Recharge = "day" },
            Resource() with { Pool = "" },
            Resource() with { Cost = 0 },
            Resource() with { Notes = new string('x', 501) },
            Resource() with { Max = new() { From = "luck" } },
            Resource() with { Max = new() { Value = 1000 } },
            Resource() with { Max = new() { From = "ability", Ability = "LUCK" } },
            Resource() with { Max = new() { From = "class-level" } },
            Resource() with { Max = new() { From = "level", Times = 0 } },
            Resource() with { Max = new() { From = "level", AtLeast = -1 } },
            Resource() with { Effect = new() { ArmorClass = 21 } },
            Resource() with { Effect = new() { Speed = -125 } },
            Resource() with { Effect = new() { Damage = "1d7 fire" } },
            Resource() with { Effect = new() { Damage = "lots" } },
            // what a JSON save can leave empty: refused with a message, not an error
            Resource() with { Id = null! },
            Resource() with { Name = null! },
            Resource() with { Max = null! },
        ];
        foreach (var r in refused)
        {
            new CharacterCombat { Resources = [r] }.Validate().Should().NotBeNull(r.ToString());
        }
        new CharacterCombat { Resources = [Resource(), Resource()] }.Validate().Should().NotBeNull("two resources with one id");
        new CharacterCombat { Resources = [Resource(), null!] }.Validate().Should().NotBeNull("an empty entry");
        new CharacterCombat { Resources = [.. Enumerable.Range(0, 31).Select(i => Resource($"r{i}"))] }.Validate().Should().NotBeNull("at most 30");
    }

    [TestMethod]
    public void Favorite_rolls_are_valid()
    {
        new CharacterCombat
        {
            FavoriteRolls = [new() { Id = "f1", Name = "Test Smite", Formula = "2d8+3" }, new() { Id = "f2", Name = "Test Luck", Formula = "d20" }],
        }.Validate().Should().BeNull();
        new CharacterCombat { FavoriteRolls = null }.Validate().Should().BeNull();
    }

    [TestMethod]
    public void Favorite_rolls_out_of_bounds_are_refused()
    {
        var roll = new CharacterFavoriteRoll { Id = "f1", Name = "Test Smite", Formula = "2d8+3" };
        CharacterFavoriteRoll[] refused =
        [
            roll with { Id = "" },
            roll with { Id = "a b" },
            roll with { Name = " " },
            roll with { Name = new string('x', 61) },
            roll with { Formula = "" },
            roll with { Formula = "2d" },
            roll with { Formula = "1d6" + string.Concat(Enumerable.Repeat("+1", 50)) },
        ];
        foreach (var r in refused)
        {
            new CharacterCombat { FavoriteRolls = [r] }.Validate().Should().NotBeNull(r.ToString());
        }
        new CharacterCombat { FavoriteRolls = [roll, roll] }.Validate().Should().NotBeNull("two rolls with one id");
        // what a JSON save can leave empty: refused with a message, not an error
        new CharacterCombat { FavoriteRolls = [roll, null!] }.Validate().Should().NotBeNull("an empty entry");
        new CharacterCombat { FavoriteRolls = [roll with { Id = null! }] }.Validate().Should().NotBeNull("no id");
        new CharacterCombat { FavoriteRolls = [roll with { Name = null! }] }.Validate().Should().NotBeNull("no name");
        new CharacterCombat { FavoriteRolls = [roll with { Formula = null! }] }.Validate().Should().NotBeNull("no dice");
        new CharacterCombat { FavoriteRolls = [.. Enumerable.Range(0, 41).Select(i => roll with { Id = $"f{i}" })] }.Validate().Should().NotBeNull("at most 40");
    }

    /// <summary>The web app's dice reader and the server agree on every case of the shared table (lib/lore/dice-cases.json).</summary>
    [TestMethod]
    public void Favorite_roll_formulas_are_dice_as_the_web_app_reads_them()
    {
        var file = SharedFile("src", "frontend", "apps", "builder-app", "src", "lib", "lore", "dice-cases.json");
        using var cases = JsonDocument.Parse(File.ReadAllText(file));
        var list = cases.RootElement.GetProperty("cases").EnumerateArray().ToList();
        list.Should().NotBeEmpty();
        foreach (var c in list)
        {
            var formula = c.GetProperty("formula").GetString()!;
            CharacterFavoriteRoll.IsDice(formula).Should().Be(c.GetProperty("dice").GetBoolean(), $"\"{formula}\"");
        }
    }

    private static string SharedFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Starlights.slnx")))
            {
                return Path.Combine([dir.FullName, .. parts]);
            }
        }
        throw new FileNotFoundException("The repository root (Starlights.slnx) was not found above the test's folder.");
    }
}
