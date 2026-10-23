using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Starlights.Integration.Drivers.CharacterCreation;
using Starlights.Integration.Extensions;
using Starlights.Modules.Characters.Data.EntityFramework;
using Starlights.Modules.Characters.Domain.Characters;
using Starlights.Modules.Elements.Data.EntityFramework;
using Starlights.Modules.Elements.Domain.Homebrew;
using Starlights.Modules.Elements.Endpoints.Installation;
using Starlights.Modules.Elements.Integration;

namespace Starlights.Integration.Tests.Elements;

/// <summary>
/// The Homebrew page's edit preview (POST /api/elements/homebrew/{kind}/preview) and the revision a save can send:
/// the preview is admin only and writes nothing; it says what changes, who uses the record and what they would lose,
/// and refuses what the save refuses; a save based on a preview is refused (409, with a fresh preview) when the record
/// changed or went away since, and a save without one works as it always did. Made-up homebrew in a temporary folder.
/// </summary>
/// <remarks>
/// On SQL Server (<c>STARFALL_TEST_SQLSERVER</c>, as <see cref="HomebrewSqlServerTests"/>): a Homebrew page save
/// imports its whole file again, and the in-memory provider keeps a removed element's components, so a second save
/// into a file cannot be shown there.
/// </remarks>
[TestClass]
[TestCategory("SqlServer")]
public sealed class HomebrewEditPreviewTests : IntegrationTestBase
{
    // what the saves said before the preview existed; they still do
    private const string SpellMessage = "A homebrew spell needs a name (100 characters at most), a level from 0 to 9, a school, and class lists made of letters.";
    private const string SubclassMessage = "A homebrew subclass needs a name, a class it belongs to, and features with a level from 1 to 20 and a name.";
    private const string ClassMessage = "A homebrew class needs a name made of letters, a hit die (d6–d12), proficiencies from the lists, levels from 1 to 20, and a spellcasting ability when it casts.";

    private IntegrationHost _integration = default!;
    private string _connection = default!;
    private string _root = default!;
    private string _homebrew = default!;

    [TestInitialize]
    public async Task Initialize()
    {
        var server = Environment.GetEnvironmentVariable("STARFALL_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(server))
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STARFALL_REQUIRE_SQLSERVER")))
            {
                Assert.Fail("STARFALL_REQUIRE_SQLSERVER is set but STARFALL_TEST_SQLSERVER is not");
            }
            Assert.Inconclusive("STARFALL_TEST_SQLSERVER is not set to a disposable SQL Server");
        }
        _connection = new SqlConnectionStringBuilder(server) { InitialCatalog = $"starfall_prev_{Guid.NewGuid():N}"[..28] }.ConnectionString;
        await using (var elements = new ElementsContext(new DbContextOptionsBuilder<ElementsContext>().UseSqlServer(_connection).Options))
        {
            await elements.Database.MigrateAsync();
        }
        await using (var characters = new CharactersContext(new DbContextOptionsBuilder<CharactersContext>().UseSqlServer(_connection).Options))
        {
            await characters.Database.MigrateAsync();
        }

        _root = Directory.CreateTempSubdirectory("starfall-homebrew-preview").FullName;
        _homebrew = Path.Combine(_root, "homebrew");
        var content = Directory.CreateDirectory(Path.Combine(_root, "content")).FullName;
        _integration = IntegrationHost.CreateDefaultBuilder(this)
            .ConfigureOptions(o =>
            {
                o.TestTimeout = TimeSpan.FromMinutes(2);
                o.Settings["Integration:SqlServer"] = _connection;
                o.Settings["Aurora:ContentPath"] = content;
                o.Settings["Aurora:HomebrewPath"] = _homebrew;
                o.Settings["Aurora:PullRequestsPath"] = Path.Combine(_root, "no-pull-requests");
                o.Settings["FiveETools:Path"] = Path.Combine(_root, "no-5etools");
            })
            .Build();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _integration?.Dispose();
        if (_connection is not null)
        {
            // as HomebrewSqlServerTests: not EnsureDeleted, which clears every connection pool
            var database = new SqlConnectionStringBuilder(_connection);
            SqlConnection.ClearPool(new SqlConnection(_connection));
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(_connection) { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            await using var drop = new SqlCommand($"ALTER DATABASE [{database.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database.InitialCatalog}];", master);
            await drop.ExecuteNonQueryAsync();
        }
        if (_root is not null)
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static HomebrewSpellModel Spell(string name = "Test Sparkle", int level = 1) => new()
    {
        Name = name,
        Source = "Test Homebrew",
        Level = level,
        School = "Evocation",
        Lists = ["Wizard"],
        Description = "A made-up spell. It deals 1d6 radiant damage.",
    };

    private string SpellsFile => Path.Combine(_homebrew, "starlights-spells.xml");

    private async Task<HomebrewEditPreview> PreviewAsync(string kind, object model)
    {
        using var admin = _integration.CreateAdminClient();
        var response = await admin.PostAsJsonAsync($"/api/elements/homebrew/{kind}/preview", model, _integration.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(_integration.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<HomebrewEditPreview>(_integration.CancellationToken))!;
    }

    private async Task<HttpResponseMessage> SaveAsync(string kind, object model)
    {
        using var admin = _integration.CreateAdminClient();
        return await admin.PutAsJsonAsync($"/api/elements/homebrew/{kind}", model, _integration.CancellationToken);
    }

    private async Task<T> SavedAsync<T>(string kind, T model)
    {
        var response = await SaveAsync(kind, model!);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(_integration.CancellationToken));
        return (await response.Content.ReadFromJsonAsync<T>(_integration.CancellationToken))!;
    }

    private async Task<List<HomebrewSpellModel>> SpellsAsync()
    {
        using var client = _integration.CreateClient();
        return (await client.GetFromJsonAsync<HomebrewSpellsResponse>("/api/elements/homebrew/spells", _integration.CancellationToken))!.Spells;
    }

    [TestMethod]
    public async Task Preview_NeedsTheAdminKey_AndWritesNothing()
    {
        await _integration.InitializeElements();
        using var anonymous = _integration.CreateClient();

        var refused = await anonymous.PostAsJsonAsync("/api/elements/homebrew/spells/preview", Spell(), _integration.CancellationToken);
        var preview = await PreviewAsync("spells", Spell());

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden, "it names the characters that use homebrew");
        preview.Should().BeEquivalentTo(new
        {
            Kind = "spells",
            Id = "ID_STARLIGHTS_HOMEBREW_SPELL_TEST_SPARKLE",
            IsNew = true,
            Revision = (string?)null,
            Changes = Array.Empty<HomebrewFieldChange>(),
            Characters = Array.Empty<HomebrewAffectedCharacter>(),
            Diagnostics = Array.Empty<ImportDiagnostic>(),
        });
        preview.Elements.Should().Equal(new HomebrewElementChange("ID_STARLIGHTS_HOMEBREW_SPELL_TEST_SPARKLE", "Test Sparkle", HomebrewElementChangeKind.Added));
        File.Exists(SpellsFile).Should().BeFalse("a preview saves nothing");
        (await SpellsAsync()).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ASavedSpell_HasARevision_AndPreviewingItUnchangedChangesNothing()
    {
        await _integration.InitializeElements();
        var saved = await SavedAsync("spells", Spell());

        var listed = (await SpellsAsync()).Single();
        var preview = await PreviewAsync("spells", listed);

        saved.Revision.Should().NotBeNullOrEmpty();
        listed.Revision.Should().Be(saved.Revision, "what a save answers is what a read gives");
        preview.IsNew.Should().BeFalse();
        preview.Revision.Should().Be(saved.Revision);
        preview.Changes.Should().BeEmpty();
        preview.Elements.Should().BeEmpty();
        preview.Diagnostics.Should().BeEmpty();
    }

    [TestMethod]
    public async Task AnEditsPreview_ListsWhatChanges_AndWhoUsesIt_AndWritesNothing()
    {
        await _integration.InitializeElements();
        var saved = await SavedAsync("spells", Spell());
        var characterId = await _integration.GetDriver<CharacterCreationDriver>().CreateCharacterAsync();
        using (var admin = _integration.CreateAdminClient())
        {
            var spell = AuroraIds.ToGuid(saved.Id!);
            var magic = await admin.PutAsJsonAsync($"/api/characters/{characterId}/magic", new CharacterMagic { Prepared = new() { ["Wizard"] = [spell] } }, _integration.CancellationToken);
            magic.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        var file = await File.ReadAllBytesAsync(SpellsFile, _integration.CancellationToken);

        var preview = await PreviewAsync("spells", saved with { Level = 2, Lists = ["Wizard", "Sorcerer"] });

        preview.Changes.Should().Equal(new HomebrewFieldChange("level", "1", "2"), new HomebrewFieldChange("lists", "Wizard", "Wizard, Sorcerer"));
        preview.Elements.Should().Equal(new HomebrewElementChange(saved.Id!, "Test Sparkle", HomebrewElementChangeKind.Changed));
        preview.Characters.Should().ContainSingle().Which.Should().BeEquivalentTo(new { CharacterId = characterId, Uses = new[] { saved.Id }, Loses = Array.Empty<string>() });
        (await File.ReadAllBytesAsync(SpellsFile, _integration.CancellationToken)).Should().Equal(file, "a preview saves nothing");
        (await SpellsAsync()).Single().Level.Should().Be(1);
    }

    [TestMethod]
    public async Task SaveExpectingNoRecord_RefusesAConcurrentCreation_AndPreservesIt()
    {
        await _integration.InitializeElements();
        var preview = await PreviewAsync("spells", Spell());
        preview.Revision.Should().BeNull();
        var concurrent = await SavedAsync("spells", Spell(level: 3));
        var file = await File.ReadAllBytesAsync(SpellsFile, _integration.CancellationToken);

        var response = await SaveAsync("spells", Spell() with { ExpectedRevision = string.Empty });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await File.ReadAllBytesAsync(SpellsFile, _integration.CancellationToken)).Should().Equal(file);
        (await SpellsAsync()).Single().Revision.Should().Be(concurrent.Revision);
    }

    [TestMethod]
    public async Task SaveExpectingNoRecord_CreatesItWhenStillAbsent()
    {
        await _integration.InitializeElements();
        var saved = await SavedAsync("spells", Spell() with { ExpectedRevision = string.Empty });
        saved.Revision.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task ASaveBasedOnAnOlderRevision_IsRefused_WithAFreshPreview()
    {
        await _integration.InitializeElements();
        var first = await SavedAsync("spells", Spell());
        // saved again elsewhere, without a revision: as before, it simply saves
        var second = await SavedAsync("spells", first with { Level = 3, Revision = null });
        var file = await File.ReadAllBytesAsync(SpellsFile, _integration.CancellationToken);

        // reviewed in a form opened on the first revision: the review says so, and compares with what is saved now
        var reviewed = await PreviewAsync("spells", first with { Level = 2 });
        reviewed.Revision.Should().Be(second.Revision);
        reviewed.Changes.Should().Equal(new HomebrewFieldChange("level", "3", "2"));
        reviewed.Diagnostics.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Severity = ImportDiagnosticSeverity.Warning, Code = ImportDiagnosticCode.StaleRevision, ElementId = first.Id });

        var stale = await SaveAsync("spells", first with { Level = 2, ExpectedRevision = first.Revision });
        var preview = (await stale.Content.ReadFromJsonAsync<HomebrewEditPreview>(_integration.CancellationToken))!;

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        preview.Revision.Should().Be(second.Revision).And.NotBe(first.Revision);
        preview.Diagnostics.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Severity = ImportDiagnosticSeverity.Error, Code = ImportDiagnosticCode.StaleRevision, ElementId = first.Id });
        preview.Changes.Should().Equal(new HomebrewFieldChange("level", "3", "2"));
        (await File.ReadAllBytesAsync(SpellsFile, _integration.CancellationToken)).Should().Equal(file, "nothing was saved");

        (await SavedAsync("spells", first with { Level = 2, ExpectedRevision = second.Revision })).Level.Should().Be(2, "with the current revision it saves");
    }

    [TestMethod]
    public async Task ASaveBasedOnARemovedRecord_IsRefused()
    {
        await _integration.InitializeElements();
        var saved = await SavedAsync("spells", Spell());
        using (var admin = _integration.CreateAdminClient())
        {
            (await admin.DeleteAsync($"/api/elements/homebrew/spells/{saved.Id}", _integration.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var stale = await SaveAsync("spells", saved with { ExpectedRevision = saved.Revision });
        var preview = (await stale.Content.ReadFromJsonAsync<HomebrewEditPreview>(_integration.CancellationToken))!;

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        preview.IsNew.Should().BeTrue();
        preview.Diagnostics.Single().Message.Should().Be("It was removed after you opened it, so it was not saved.");
        (await SpellsAsync()).Should().BeEmpty();
    }

    [TestMethod]
    public async Task ANewRecordNamedLikeASavedOne_IsFlagged()
    {
        await _integration.InitializeElements();
        var saved = await SavedAsync("spells", Spell());

        var preview = await PreviewAsync("spells", Spell(level: 4));

        preview.IsNew.Should().BeFalse();
        preview.Diagnostics.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Severity = ImportDiagnosticSeverity.Warning, Code = ImportDiagnosticCode.ReplacesExisting, ElementId = saved.Id });
    }

    /// <summary>The messages of a refused save's answer (FastEndpoints' errors), as written.</summary>
    private async Task<List<string>> ErrorsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>(_integration.CancellationToken))!["errors"]!.AsObject()
            .SelectMany(e => e.Value!.AsArray().Select(m => m!.GetValue<string>())).ToList();

    /// <summary>Each model previewed and saved: the save refuses it exactly when the preview reports an error, with its old message.</summary>
    private async Task AgreeAsync<T>(string kind, string message, IEnumerable<(string Case, T Model)> cases) where T : class
    {
        foreach (var (name, model) in cases)
        {
            var preview = await PreviewAsync(kind, model);
            var save = await SaveAsync(kind, model);
            var refused = preview.Diagnostics.Any(d => d.Severity == ImportDiagnosticSeverity.Error);

            (save.StatusCode == HttpStatusCode.BadRequest).Should().Be(refused, "the preview and the save agree on {0} ({1})", name, save.StatusCode);
            preview.Diagnostics.Where(d => d.Severity == ImportDiagnosticSeverity.Error).Should().OnlyContain(d => d.Code == ImportDiagnosticCode.InvalidValue && d.Detail != null);
            if (refused)
            {
                (await ErrorsAsync(save)).Should().Equal([message], "the save's message for {0} is as it was", name);
                preview.Elements.Should().BeEmpty("nothing to compare when it cannot be saved");
            }
        }
    }

    private const string SpellMechanicsMessage =
        "A homebrew spell's mechanics need a known roll, the ability a saving throw uses, damage like \"2d6 fire\", healing like \"2d8\", cantrip scaling like \"5:2d10\" (cantrips only) and slot scaling like \"1d6\" (leveled spells only).";

    [TestMethod]
    public async Task ThePreviewAndTheSave_RefuseTheSameSpellMechanics()
    {
        await _integration.InitializeElements();
        await AgreeAsync("spells", SpellMechanicsMessage, new (string, HomebrewSpellModel)[]
        {
            ("an attack with damage", Spell() with { Roll = "ranged-attack", Damage = "1d10 cold" }),
            ("healing that grows with the slot", Spell() with { Roll = "none", Healing = "2d8", AddsModifier = true, SlotScaling = "1d8" }),
            ("a cantrip whose damage grows", Spell(level: 0) with { Roll = "ranged-attack", Damage = "1d10 cold", CantripScaling = "5:2d10; 11:3d10; 17:4d10" }),
            ("an unknown roll", Spell() with { Roll = "fireball" }),
            ("a save without its ability", Spell() with { Roll = "save", Damage = "8d6 fire" }),
            ("an ability without a save", Spell() with { Roll = "melee-attack", SaveAbility = "Dexterity" }),
            ("a damage type there is not", Spell() with { Roll = "save", SaveAbility = "Dexterity", Damage = "2d6 frost" }),
            ("damage without a roll", Spell() with { Damage = "2d6 fire" }),
            ("cantrip scaling on a 1st-level spell", Spell() with { Roll = "ranged-attack", Damage = "1d10 cold", CantripScaling = "5:2d10" }),
            ("slot scaling on a cantrip", Spell(level: 0) with { Roll = "ranged-attack", Damage = "1d10 cold", SlotScaling = "1d10" }),
            ("cantrip levels going down", Spell(level: 0) with { Roll = "ranged-attack", Damage = "1d10 cold", CantripScaling = "11:3d10; 5:2d10" }),
            ("healing that is not dice", Spell() with { Roll = "none", Healing = "lots" }),
        });

        // a field of the form and one of the mechanics: the message the save always gave, then the mechanics' own
        var both = await SaveAsync("spells", Spell(name: " ") with { Roll = "fireball" });
        both.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorsAsync(both)).Should().Equal(SpellMessage, SpellMechanicsMessage);
    }

    [TestMethod]
    public async Task ASpellsMechanics_AreSavedAndRead_AndASpellWithoutThemKeepsItsRevision()
    {
        await _integration.InitializeElements();
        var plain = await SavedAsync("spells", Spell());
        var again = await SavedAsync("spells", plain with { ExpectedRevision = plain.Revision });
        again.Revision.Should().Be(plain.Revision, "saved again as it was, it is written as it was");

        var mechanics = plain with { Roll = "save", SaveAbility = "Dexterity", HalfOnSave = true, Damage = "3d6 fire" };
        var preview = await PreviewAsync("spells", mechanics);
        preview.Changes.Should().Equal(
            new HomebrewFieldChange("roll", null, "save"), new HomebrewFieldChange("saveAbility", null, "Dexterity"),
            new HomebrewFieldChange("halfOnSave", "no", "yes"), new HomebrewFieldChange("damage", null, "3d6 fire"));

        var saved = await SavedAsync("spells", mechanics with { ExpectedRevision = plain.Revision });
        saved.Should().BeEquivalentTo(new { Roll = "save", SaveAbility = "Dexterity", HalfOnSave = true, Damage = "3d6 fire", Healing = (string?)null });
        saved.Revision.Should().NotBe(plain.Revision);
        (await SpellsAsync()).Single().Should().BeEquivalentTo(new { Roll = "save", Damage = "3d6 fire" });
    }

    [TestMethod]
    public async Task ThePreviewAndTheSave_RefuseTheSameSpells()
    {
        await _integration.InitializeElements();
        await AgreeAsync("spells", SpellMessage, new (string, HomebrewSpellModel)[]
        {
            ("valid", Spell()),
            ("blank name", Spell(name: " ")),
            ("long name", Spell(name: new string('x', 101))),
            ("level 10", Spell(level: 10)),
            ("unknown school", Spell() with { School = "Chronurgy" }),
            ("long range", Spell() with { Range = new string('x', 101) }),
            ("long material", Spell() with { MaterialText = new string('x', 301) }),
            ("long higher levels", Spell() with { HigherLevels = new string('x', 5_001) }),
            ("bad list name", Spell() with { Lists = ["Wizard!"] }),
            ("21 lists", Spell() with { Lists = [.. Enumerable.Range(0, 21).Select(i => $"List {i}")] }),
            ("valid at the limits", Spell(name: "Test Second", level: 0) with { Lists = [], MaterialText = new string('x', 300) }),
        });
    }

    private static HomebrewClassModel Class(string name = "Test Warden") =>
        new() { Name = name, Source = "Test Homebrew", HitDie = 10, SavingThrows = ["Strength", "Wisdom"], Armor = ["Light"], Weapons = ["Simple"], Skills = ["Athletics"], SkillChoices = 1 };

    [TestMethod]
    public async Task ThePreviewAndTheSave_RefuseTheSameClasses()
    {
        await _integration.InitializeElements();
        await AgreeAsync("classes", ClassMessage, new (string, HomebrewClassModel)[]
        {
            ("valid", Class()),
            ("a d7", Class() with { HitDie = 7 }),
            ("a name with a star", Class("Test*Warden")),
            ("four saving throws", Class() with { SavingThrows = ["Strength", "Dexterity", "Wisdom", "Charisma"] }),
            ("an unknown saving throw", Class() with { SavingThrows = ["Luck"] }),
            ("multiclass armor it lacks", Class() with { MulticlassArmor = ["Heavy"] }),
            ("an unknown skill", Class() with { Skills = ["Juggling"] }),
            ("seven skill choices", Class() with { SkillChoices = 7 }),
            ("an unknown multiclass ability", Class() with { MulticlassAbility = "Luck" }),
            ("subclass at 21", Class() with { SubclassLevel = 21 }),
            ("an improvement at 0", Class() with { AsiLevels = [0, 4] }),
            ("casting without an ability", Class() with { Casting = "Full", CastingAbility = null }),
            ("unknown casting", Class() with { Casting = "Quarter" }),
            ("a bad spell list name", Class() with { SpellLists = ["Wizard!"] }),
            ("seven cantrips", Class() with { Cantrips = 7 }),
            ("a feature at 21", Class() with { Features = [new() { Level = 21, Name = "Late" }] }),
            ("a casting class at the limits", Class("Test Seer") with { Casting = "Pact", CastingAbility = "Charisma", SpellLists = ["Wizard"], Cantrips = 6 }),
        });
    }

    [TestMethod]
    public async Task ThePreviewAndTheSave_RefuseTheSameSubclasses()
    {
        await _integration.InitializeElements();
        var cls = await SavedAsync("classes", Class());
        HomebrewSubclassModel Subclass() => new() { Name = "Test Circle", Source = "Test Homebrew", ClassSupports = cls.SubclassSupports!, ClassName = cls.Name, Features = [new() { Level = 3, Name = "Test Ward" }] };

        await AgreeAsync("subclasses", SubclassMessage, new (string, HomebrewSubclassModel)[]
        {
            ("valid", Subclass()),
            ("an unknown class", Subclass() with { ClassSupports = "Nobody Subclass" }),
            ("a feature without a name", Subclass() with { Features = [new() { Level = 3, Name = " " }] }),
            ("a feature at 0", Subclass() with { Features = [new() { Level = 0, Name = "Early" }] }),
            ("a long name", Subclass() with { Name = new string('x', 101) }),
            ("41 features", Subclass() with { Features = [.. Enumerable.Range(1, 41).Select(i => new HomebrewFeatureModel { Level = 3, Name = $"Feature {i}" })] }),
        });
    }

    [TestMethod]
    public async Task ASubclassEdit_ThatDropsAFeature_SaysWhoLosesIt()
    {
        await _integration.InitializeElements();
        var cls = await SavedAsync("classes", new HomebrewClassModel { Name = "Test Warden", Source = "Test Homebrew", HitDie = 10, SavingThrows = ["Strength", "Wisdom"] });
        var subclass = await SavedAsync("subclasses", new HomebrewSubclassModel
        {
            Name = "Test Circle",
            Source = "Test Homebrew",
            ClassSupports = cls.SubclassSupports!,
            ClassName = cls.Name,
            Features = [new() { Level = 3, Name = "Test Ward" }, new() { Level = 6, Name = "Test Echo" }],
        });
        var echo = subclass.Features.Single(f => f.Name == "Test Echo").Id!;
        var characterId = await _integration.GetDriver<CharacterCreationDriver>().CreateCharacterAsync();
        using (var admin = _integration.CreateAdminClient())
        {
            // the preview asks who uses the feature's element however they came by it; a prepared entry stands in for
            // a builder pick here (picks are found by their registrations, which the SQL Server suite covers)
            var magic = new CharacterMagic { Prepared = new() { ["Test"] = [AuroraIds.ToGuid(echo)] } };
            (await admin.PutAsJsonAsync($"/api/characters/{characterId}/magic", magic, _integration.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var preview = await PreviewAsync("subclasses", subclass with { Features = [.. subclass.Features.Where(f => f.Id != echo)] });

        preview.Elements.Should().Equal(
            new HomebrewElementChange(subclass.Id!, "Test Circle", HomebrewElementChangeKind.Changed),
            new HomebrewElementChange(echo, "Level 6: Test Echo", HomebrewElementChangeKind.Removed));
        preview.Characters.Should().ContainSingle().Which.Should().BeEquivalentTo(new { CharacterId = characterId, Uses = new[] { echo }, Loses = new[] { echo } });
        preview.Changes.Should().BeEmpty("the features are listed as elements, not as a field");
    }

    [TestMethod]
    public async Task ASpellTakenOffAList_SaysWhoHasItPreparedFromThatList()
    {
        await _integration.InitializeElements();
        var saved = await SavedAsync("spells", Spell() with { Lists = ["Wizard", "Sorcerer"] });
        var spell = AuroraIds.ToGuid(saved.Id!);
        var wizard = await PreparedAsync("Wizard", spell);
        var sorcerer = await PreparedAsync("Sorcerer", spell);

        var preview = await PreviewAsync("spells", saved with { Lists = ["Sorcerer"] });

        preview.Characters.Should().HaveCount(2);
        preview.Characters.Single(c => c.CharacterId == wizard).Invalidated.Should().Equal(new HomebrewInvalidatedPick(saved.Id!, "Test Sparkle",
            "Prepared as a Wizard spell, but it would no longer be on the Wizard list. It stays prepared until it is changed."));
        preview.Characters.Single(c => c.CharacterId == sorcerer).Invalidated.Should().BeEmpty("it stays on the Sorcerer list");
        preview.Characters.Should().OnlyContain(c => c.Loses.Count == 0, "the spell itself stays");
    }

    [TestMethod]
    public async Task ASubclassMovedToAnotherClassesChoice_SaysWhoPickedItForTheOldOne()
    {
        await _integration.InitializeElements();
        var warden = await SavedAsync("classes", new HomebrewClassModel { Name = "Test Warden", Source = "Test Homebrew", HitDie = 10, SavingThrows = ["Strength", "Wisdom"] });
        var keeper = await SavedAsync("classes", new HomebrewClassModel { Name = "Test Keeper", Source = "Test Homebrew", HitDie = 8, SavingThrows = ["Dexterity", "Intelligence"] });
        warden.SubclassSupports.Should().Be("Test Warden Subclass");
        keeper.SubclassSupports.Should().Be("Test Keeper Subclass");
        var subclass = await SavedAsync("subclasses", new HomebrewSubclassModel
        {
            Name = "Test Circle", Source = "Test Homebrew", ClassSupports = warden.SubclassSupports!, ClassName = warden.Name, Features = [new() { Level = 3, Name = "Test Ward" }],
        });
        // a prepared entry stands in for the builder pick (see ASubclassEdit_ThatDropsAFeature_SaysWhoLosesIt)
        var characterId = await PreparedAsync("Test", AuroraIds.ToGuid(subclass.Id!));

        var preview = await PreviewAsync("subclasses", subclass with { ClassSupports = keeper.SubclassSupports!, ClassName = keeper.Name });

        preview.Characters.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            CharacterId = characterId,
            Uses = new[] { subclass.Id },
            Loses = Array.Empty<string>(),
            Invalidated = new[]
            {
                new HomebrewInvalidatedPick(subclass.Id!, "Test Circle",
                    "Picked for the \"Test Warden Subclass\" choice, but it would answer \"Test Keeper Subclass\" instead. It stays picked, but could not be picked there again."),
            },
        });
    }

    [TestMethod]
    public async Task AClassWithANewSubclassKey_SaysWhoseSubclassNoLongerFits()
    {
        await _integration.InitializeElements();
        var warden = await SavedAsync("classes", new HomebrewClassModel { Name = "Test Warden", Source = "Test Homebrew", HitDie = 10, SavingThrows = ["Strength", "Wisdom"] });
        var subclass = await SavedAsync("subclasses", new HomebrewSubclassModel
        {
            Name = "Test Circle", Source = "Test Homebrew", ClassSupports = warden.SubclassSupports!, ClassName = warden.Name, Features = [new() { Level = 3, Name = "Test Ward" }],
        });
        var characterId = await PreparedAsync("Test", AuroraIds.ToGuid(subclass.Id!));

        var unchanged = await PreviewAsync("classes", warden with { HitDie = 12 });
        var preview = await PreviewAsync("classes", warden with { SubclassSupports = "Warden Path" });

        unchanged.Characters.Should().BeEmpty("the subclass still answers the class's choice");
        preview.Characters.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            CharacterId = characterId,
            Uses = Array.Empty<string>(),
            Loses = Array.Empty<string>(),
            Invalidated = new[]
            {
                new HomebrewInvalidatedPick(subclass.Id!, "Test Circle",
                    "Picked as a Test Warden subclass, but the class would ask for \"Warden Path\" subclasses instead of \"Test Warden Subclass\". It stays picked, but could not be picked again."),
            },
        });
    }

    /// <summary>A new character with the element prepared under that spellcasting.</summary>
    private async Task<Guid> PreparedAsync(string casting, Guid element)
    {
        var characterId = await _integration.GetDriver<CharacterCreationDriver>().CreateCharacterAsync();
        using var admin = _integration.CreateAdminClient();
        var magic = new CharacterMagic { Prepared = new() { [casting] = [element] } };
        (await admin.PutAsJsonAsync($"/api/characters/{characterId}/magic", magic, _integration.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        return characterId;
    }
}
