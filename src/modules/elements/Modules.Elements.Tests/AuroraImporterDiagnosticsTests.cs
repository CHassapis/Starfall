using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Starlights.Modules.Elements.Data;
using Starlights.Modules.Elements.Domain;
using Starlights.Modules.Elements.Integration;
using Starlights.Modules.Elements.Services;
using Starlights.Modules.Elements.Services.FiveETools;
using Starlights.Platform.Data;

namespace Starlights.Modules.Elements.Tests;

[TestClass]
public class AuroraImporterDiagnosticsTests
{
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Directory.CreateTempSubdirectory("aurora-diagnostics-tests").FullName;
        var core = Directory.CreateDirectory(Path.Combine(_root, "content", "core")).FullName;
        Directory.CreateDirectory(Path.Combine(_root, "content", "other"));
        Directory.CreateDirectory(Path.Combine(_root, "homebrew"));

        File.WriteAllText(Path.Combine(core, "feats.xml"), """
            <elements>
              <element name="Test Feat" type="Feat" source="Test" id="ID_TEST_FEAT">
                <rules><grant type="Proficiency" id="ID_TEST_NOT_THERE" /></rules>
              </element>
            </elements>
            """);
        File.WriteAllText(Path.Combine(core, "broken.xml"), "<elements><element name=\"Half");
        File.WriteAllText(Path.Combine(_root, "content", "other", "broken-too.xml"), "<elements>");
        File.WriteAllText(Path.Combine(core, "test.index"), """
            <index>
              <files>
                <file name="feats.xml" url="https://raw.githubusercontent.com/example/elements/master/core/feats.xml" />
                <file name="broken.xml" url="https://raw.githubusercontent.com/example/elements/master/core/broken.xml" />
                <file name="gone.xml" url="https://raw.githubusercontent.com/example/elements/master/core/gone.xml" />
              </files>
            </index>
            """);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    private void Homebrew(string file, string content) => File.WriteAllText(Path.Combine(_root, "homebrew", file), content);

    private async Task<AuroraImportResult> ImportAsync(string index, string fiveETools = "no-5etools")
    {
        var repository = new Mock<IElementsRepository>();
        repository.Setup(x => x.GetElementSummariesAsync()).ReturnsAsync([]);
        var persistence = new Mock<IPersistence>();
        persistence.Setup(x => x.GetRepository<IElementsRepository>()).Returns(repository.Object);
        var importer = new AuroraImporter(
            NullLogger<AuroraImporter>.Instance,
            persistence.Object,
            new AuroraImporterOptions(Path.Combine(_root, "content"), HomebrewPath: Path.Combine(_root, "homebrew")),
            new FiveEToolsData(new FiveEToolsOptions(Path.Combine(_root, fiveETools)), NullLogger<FiveEToolsData>.Instance));
        return await importer.ImportAsync(index);
    }

    [TestMethod]
    public async Task HomebrewImport_ReportsWhatItRanInto_AndKeepsTheTextWarnings()
    {
        Homebrew("my-feats.xml", """
            <elements>
              <element name="Brew Feat" type="Feat" source="Brew" id="ID_BREW_FEAT">
                <rules><grant type="Proficiency" id="ID_BREW_MISSING" /></rules>
              </element>
            </elements>
            """);
        Homebrew("patch.xml", """<elements><append id="ID_NOBODY_HAS_THIS"><supports>Brew</supports></append></elements>""");
        Homebrew("torn.xml", "<elements><element");

        var result = await ImportAsync(AuroraImporterIndex.Homebrew);

        result.Warnings.Should().Equal("homebrew/patch.xml: append target ID_NOBODY_HAS_THIS was not found in the loaded Aurora source files. Install its source content, then upload this patch again.");
        result.Diagnostics.Select(d => (d.Severity, d.Code, d.Importer, d.File, d.ElementId, d.Detail)).Should().Equal(
            (ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.UnreadableFile, ImportDiagnosticImporter.Homebrew, "homebrew/torn.xml", (string?)null, result.Diagnostics[0].Detail),
            (ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.AppendTargetMissing, ImportDiagnosticImporter.Homebrew, "homebrew/patch.xml", "ID_NOBODY_HAS_THIS", (string?)null),
            (ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.GrantUnresolved, ImportDiagnosticImporter.Homebrew, "homebrew/my-feats.xml", "ID_BREW_FEAT", "ID_BREW_MISSING"));
        result.Diagnostics[0].Detail.Should().NotBeNullOrEmpty("the XML error is kept");
        result.Diagnostics[2].Message.Should().Be("Brew Feat grants ID_BREW_MISSING, which is not in the content; that grant was left out.");
        result.Diagnostics[2].ElementName.Should().Be("Brew Feat");
        result.SkippedUnresolvedGrants.Should().Be(1, "the count and the diagnostics say the same");
    }

    [TestMethod]
    public async Task HomebrewImport_DoesNotReportTheContentRepositorysBrokenFiles()
    {
        Homebrew("fine.xml", """<elements><element name="Fine" type="Feat" source="Brew" id="ID_BREW_FINE"><rules><stat name="strength" value="1" /></rules></element></elements>""");

        var result = await ImportAsync(AuroraImporterIndex.Homebrew);

        result.Diagnostics.Should().BeEmpty("core/broken.xml and other/broken-too.xml are not part of a homebrew import");
    }

    [TestMethod]
    public async Task IndexImport_ReportsMissingEntries_AndBrokenFilesItLists_Only()
    {
        var result = await ImportAsync("core/test.index");

        result.Diagnostics.Select(d => (d.Code, d.File, d.Detail ?? d.ElementId)).Should().Equal(
            (ImportDiagnosticCode.UnreadableFile, "core/broken.xml", result.Diagnostics[0].Detail),
            (ImportDiagnosticCode.IndexEntryMissing, "core/test.index", "gone.xml"),
            (ImportDiagnosticCode.GrantUnresolved, "core/feats.xml", "ID_TEST_NOT_THERE"));
        result.Diagnostics.Should().OnlyContain(d => d.Importer == ImportDiagnosticImporter.Aurora && d.Severity == ImportDiagnosticSeverity.Warning);
        result.Diagnostics.Should().NotContain(d => d.File == "other/broken-too.xml", "the index does not list it");
    }

    [TestMethod]
    public async Task Diagnostics_AreInTheSameOrder_WhateverOrderTheFilesAreRead()
    {
        foreach (var name in new[] { "b.xml", "a.xml", "c.xml" })
        {
            Homebrew(name, $"""<elements><element name="{name}" type="Feat" source="Brew" id="ID_BREW_{name[0]}"><rules><grant type="Feat" id="ID_GONE_{name[0]}" /></rules></element></elements>""");
        }

        var first = await ImportAsync(AuroraImporterIndex.Homebrew);
        var second = await ImportAsync(AuroraImporterIndex.Homebrew);

        first.Diagnostics.Select(d => d.File).Should().Equal("homebrew/a.xml", "homebrew/b.xml", "homebrew/c.xml");
        second.Diagnostics.Should().Equal(first.Diagnostics);
    }

    // a made-up 5etools data folder: a class with a feature the data does not have, a subclass of it with the same, and a
    // subclass of a class that is nowhere
    private string FiveEToolsDataFolder()
    {
        var data = Directory.CreateDirectory(Path.Combine(_root, "5etools-data")).FullName;
        Directory.CreateDirectory(Path.Combine(data, "class"));
        File.WriteAllText(Path.Combine(data, "deities.json"), """{"deity": []}""");
        File.WriteAllText(Path.Combine(data, "books.json"), """{"book": [{"id": "TST", "name": "Test Book", "published": "2020-01-01"}]}""");
        File.WriteAllText(Path.Combine(data, "class", "class-tester.json"), """
            {
              "class": [{ "name": "Tester", "source": "TST", "hd": { "faces": 8 }, "classFeatures": ["Testing|Tester|TST|1", "Vanished|Tester|TST|2"] }],
              "classFeature": [{ "name": "Testing", "source": "TST", "className": "Tester", "classSource": "TST", "level": 1, "entries": ["You test."] }],
              "subclass": [
                { "name": "Kept Path", "shortName": "Kept", "source": "TST", "className": "Tester", "classSource": "TST",
                  "subclassFeatures": ["Kept Path|Tester|TST|Kept|TST|3", "Gone Thing|Tester|TST|Kept|TST|6"] },
                { "name": "Lost Path", "shortName": "Lost", "source": "TST", "className": "Nobody", "classSource": "TST", "subclassFeatures": [] }
              ],
              "subclassFeature": [{ "name": "Kept Path", "source": "TST", "className": "Tester", "classSource": "TST", "subclassShortName": "Kept",
                "subclassSource": "TST", "level": 3, "entries": ["You keep."] }]
            }
            """);
        return "5etools-data";
    }

    [TestMethod]
    public async Task FiveEToolsImport_ReportsWhatTheDataRefersToAndDoesNotHave()
    {
        var data = FiveEToolsDataFolder();

        var result = await ImportAsync(AuroraImporterOptions.FiveEToolsIndex, data);

        // what it left out (the features it made with no rules are reported apart, as text only)
        result.Diagnostics.Where(d => d.Importer == ImportDiagnosticImporter.FiveETools && d.Severity == ImportDiagnosticSeverity.Warning).Select(d => (d.Code, d.ElementId, d.ElementName, d.Detail)).Should().Equal(
            (ImportDiagnosticCode.ClassFeatureMissing, "ID_5ETOOLS_CLASS_TST_TESTER", "Tester", "Vanished|Tester|TST|2"),
            (ImportDiagnosticCode.ClassFeatureMissing, "ID_5ETOOLS_SUBCLASS_TST_TESTER_KEPT_PATH", "Kept Path", "Gone Thing|Tester|TST|Kept|TST|6"),
            (ImportDiagnosticCode.SubclassClassMissing, (string?)null, "Lost Path", "Nobody|TST"));
        result.Diagnostics.Should().OnlyContain(d => d.File == "5etools/content.xml");
        result.Diagnostics.Where(d => d.Code == ImportDiagnosticCode.TextOnly).Should().NotContain(d => d.ElementId == "ID_5ETOOLS_CLASS_TST_TESTER", "a class is not a feature");
        result.Diagnostics.Where(d => d.Severity != ImportDiagnosticSeverity.Info).Should().OnlyContain(d => d.Severity == ImportDiagnosticSeverity.Warning);
        result.Diagnostics.Single(d => d.Code == ImportDiagnosticCode.SubclassClassMissing).Message
            .Should().Be("Lost Path is a subclass of Nobody (Test Book), a class neither Aurora nor 5etools has here; it was left out.");
    }

    [TestMethod]
    public async Task OtherImports_DoNotRepeatWhatThe5etoolsTranslationRanInto()
    {
        var data = FiveEToolsDataFolder();
        Homebrew("fine.xml", """<elements><element name="Fine" type="Feat" source="Brew" id="ID_BREW_FINE"><rules><stat name="strength" value="1" /></rules></element></elements>""");

        (await ImportAsync(AuroraImporterIndex.Homebrew, data)).Diagnostics.Should().BeEmpty();
        (await ImportAsync("core/test.index", data)).Diagnostics.Should().NotContain(d => d.Importer == ImportDiagnosticImporter.FiveETools);
    }

    [TestMethod]
    public async Task HomebrewImport_CountsEachEntryOnce_InFullInPartTextOnlyOrSkipped()
    {
        Homebrew("a.xml", """
            <elements>
              <element name="Brew Grantor" type="Feat" source="Brew" id="ID_BREW_GRANTOR"><rules><grant type="Feat" id="ID_BREW_MISSING" /></rules></element>
              <element name="Brew Stat" type="Feat" source="Brew" id="ID_BREW_STAT"><rules><stat name="strength" value="1" /></rules></element>
              <element name="Brew Story" type="Feat" source="Brew" id="ID_BREW_STORY"><description><p>Only words.</p></description></element>
              <element name="Brew Daily" type="Class Feature" source="Brew" id="ID_BREW_DAILY"><sheet usage="1/Long Rest"><description>Once a day.</description></sheet></element>
              <element name="Brew Picky" type="Feat" source="Brew" id="ID_BREW_PICKY">
                <requirements>ID_BREW_NOWHERE,[str:13]</requirements>
                <rules><stat name="dexterity" value="1" requirements="[odd thing]" /></rules>
              </element>
              <element name="Nameless" type="Feat" source="Brew" />
              <element name="Brew Twice" type="Feat" source="Brew" id="ID_BREW_TWICE"><rules><stat name="wisdom" value="1" /></rules></element>
              <element name="Brew Twice" type="Feat" source="Brew" id="ID_BREW_TWICE"><rules><stat name="wisdom" value="2" /></rules></element>
            </elements>
            """);
        // the homebrew copy of a feat the content repository has: it takes its place on purpose
        Homebrew("c.xml", """<elements><element name="Test Feat" type="Feat" source="Brew" id="ID_TEST_FEAT"><rules><stat name="charisma" value="1" /></rules></element></elements>""");

        var result = await ImportAsync(AuroraImporterIndex.Homebrew);

        // in full: Brew Stat, Brew Daily (its uses), Brew Twice (one copy), Test Feat; in part: Brew Grantor (its grant),
        // Brew Picky (two requirement terms); text only: Brew Story; skipped: Nameless, Brew Twice's second copy, the
        // repository's Test Feat
        result.Entries.Should().Be(new ImportEntryCounts(Complete: 4, Partial: 2, TextOnly: 1, Skipped: 3));
        result.Diagnostics.Select(d => (d.Severity, d.Code, d.File, d.ElementId, d.Detail)).Should().Equal(
            (ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.GrantUnresolved, "homebrew/a.xml", "ID_BREW_GRANTOR", "ID_BREW_MISSING"),
            (ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.DuplicateId, "homebrew/a.xml", "ID_BREW_TWICE", "homebrew/a.xml"),
            (ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.ElementIncomplete, "homebrew/a.xml", (string?)null, "id"),
            (ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.RequirementUnsupported, "homebrew/a.xml", "ID_BREW_PICKY", "ID_BREW_NOWHERE"),
            (ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.RequirementUnsupported, "homebrew/a.xml", "ID_BREW_PICKY", "[odd thing]"),
            (ImportDiagnosticSeverity.Info, ImportDiagnosticCode.DuplicateId, "core/feats.xml", "ID_TEST_FEAT", "homebrew/c.xml"),
            (ImportDiagnosticSeverity.Info, ImportDiagnosticCode.TextOnly, "homebrew/a.xml", "ID_BREW_STORY", (string?)null));
        result.Diagnostics.Select(d => d.Message).Should().Equal(
            "Brew Grantor grants ID_BREW_MISSING, which is not in the content; that grant was left out.",
            "Brew Twice (ID_BREW_TWICE) is defined twice in homebrew/a.xml; the first is used and the other left out.",
            "An element in homebrew/a.xml has no id, so it was skipped.",
            "Brew Picky's requirements name ID_BREW_NOWHERE, which is not in the content: it counts as something no character has.",
            "Brew Picky's requirements use [odd thing], which Starfall cannot check: it counts as never true.",
            "Test Feat (ID_TEST_FEAT) is defined in both core/feats.xml and homebrew/c.xml; the copy in homebrew/c.xml is used and the other left out.",
            "Brew Story has no rules: Starfall shows its description, but applies nothing from it.");
        result.Diagnostics.Single(d => d.Code == ImportDiagnosticCode.ElementIncomplete).ElementName.Should().Be("Nameless");
    }

    [TestMethod]
    public async Task StarfallsOwnContent_RepeatsNoId_OfTheContent()
    {
        (await ImportAsync(AuroraImporterOptions.BuiltInIndex)).Diagnostics.Should().NotContain(d => d.Code == ImportDiagnosticCode.DuplicateId);
        (await ImportAsync("core/test.index")).Diagnostics.Should().NotContain(d => d.Code == ImportDiagnosticCode.DuplicateId);
    }

    [TestMethod]
    public async Task FiveEToolsImport_CountsWhatItLeavesOut_OnceAnEntry_ByReason()
    {
        var data = Directory.CreateDirectory(Path.Combine(_root, "5etools-skips")).FullName;
        Directory.CreateDirectory(Path.Combine(data, "class"));
        File.WriteAllText(Path.Combine(data, "deities.json"), """{"deity": []}""");
        File.WriteAllText(Path.Combine(data, "books.json"), """{"book": [{"id": "TST", "name": "Test Book", "published": "2020-01-01"}]}""");
        File.WriteAllText(Path.Combine(data, "class", "class-skips.json"), """
            {
              "class": [
                { "name": "Lab Rat", "source": "UATest", "hd": { "faces": 8 } },
                { "name": "Old Tester", "source": "TST", "reprintedAs": ["Tester|XTST"], "hd": { "faces": 8 } },
                { "name": "Sidekick", "source": "TST", "isSidekick": true, "hd": { "faces": 8 } }
              ]
            }
            """);
        // Aurora's core content has a Test Feat
        File.WriteAllText(Path.Combine(data, "feats.json"), """{"feat": [{ "name": "Test Feat", "source": "TST", "entries": ["The same feat."] }]}""");
        // an Unearthed Arcana subrace of a 5etools species: looked at for the species and again for subraces of Aurora's
        File.WriteAllText(Path.Combine(data, "races.json"), """
            {
              "race": [{ "name": "Testfolk", "source": "TST", "size": ["M"], "speed": 30, "entries": ["Folk."] }],
              "subrace": [{ "name": "Hill", "source": "UATest", "raceName": "Testfolk", "raceSource": "TST", "entries": ["Hills."] }]
            }
            """);

        var result = await ImportAsync(AuroraImporterOptions.FiveEToolsIndex, "5etools-skips");

        result.Diagnostics.Where(d => d.Code == ImportDiagnosticCode.EntriesSkipped).Select(d => (d.Severity, d.Detail, d.Count, d.Message)).Should().Equal(
            (ImportDiagnosticSeverity.Info, "aurora-has-it", 1, "1 5etools entry left out: already in the Aurora content, whose version is used."),
            (ImportDiagnosticSeverity.Info, "not-for-players", 1, "1 5etools entry left out: not for player characters (sidekicks, NPC species)."),
            (ImportDiagnosticSeverity.Info, "reprinted", 1, "1 5etools entry left out: reprinted in a later book, whose printing is used instead."),
            (ImportDiagnosticSeverity.Info, "unearthed-arcana", 2, "2 5etools entries left out: Unearthed Arcana or Plane Shift, which Starfall leaves out like the Aurora import."));
        result.Diagnostics.Where(d => d.Code == ImportDiagnosticCode.EntriesSkipped).Should().OnlyContain(d => d.Importer == ImportDiagnosticImporter.FiveETools && d.File == "5etools/content.xml");
        result.Entries.Skipped.Should().Be(5);
        (await ImportAsync(AuroraImporterIndex.Homebrew, "5etools-skips")).Diagnostics.Should().NotContain(d => d.Code == ImportDiagnosticCode.EntriesSkipped);
    }

    [TestMethod]
    public void EntryCounts_PutEachElementInOneBucket()
    {
        ImportDiagnostic Warn(ImportDiagnosticCode code, string? id, string detail = "") =>
            new(ImportDiagnosticSeverity.Warning, code, ImportDiagnosticImporter.Homebrew, "made up") { ElementId = id, Detail = detail };
        var diagnostics = new[]
        {
            Warn(ImportDiagnosticCode.RequirementUnsupported, "A", "ID_X"),
            Warn(ImportDiagnosticCode.RequirementUnsupported, "A", "ID_X"),
            Warn(ImportDiagnosticCode.GrantUnresolved, "C"),
            new ImportDiagnostic(ImportDiagnosticSeverity.Info, ImportDiagnosticCode.TextOnly, ImportDiagnosticImporter.Homebrew, "made up") { ElementId = "B" },
            new ImportDiagnostic(ImportDiagnosticSeverity.Info, ImportDiagnosticCode.DuplicateId, ImportDiagnosticImporter.Homebrew, "made up") { ElementId = "D" },
            new ImportDiagnostic(ImportDiagnosticSeverity.Info, ImportDiagnosticCode.EntriesSkipped, ImportDiagnosticImporter.FiveETools, "made up") { Count = 7 },
        };

        // A: partial (its requirement); B: text only; C: partial although it is text only too; D: in full (the copy of its
        // id that was left out is the skipped one); skipped: D's other copy and the 7 tallied
        ImportEntryCounts.Of([("A", false), ("B", true), ("C", true), ("D", false)], diagnostics)
            .Should().Be(new ImportEntryCounts(Complete: 1, Partial: 2, TextOnly: 1, Skipped: 8));
        (new ImportEntryCounts(1, 2, 3, 4) + new ImportEntryCounts(10, 20, 30, 40)).Should().Be(new ImportEntryCounts(11, 22, 33, 44));
    }

    [TestMethod]
    public void Arrange_ListsAtMostFiftyOfACode_AndSaysHowManyMore()
    {
        var grants = Enumerable.Range(0, 120).Select(i => new ImportDiagnostic(ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.GrantUnresolved, ImportDiagnosticImporter.Aurora, $"grant {i:000}")
        {
            File = "core/a.xml",
            ElementId = $"ID_{i:000}",
        });
        var unreadable = new ImportDiagnostic(ImportDiagnosticSeverity.Warning, ImportDiagnosticCode.UnreadableFile, ImportDiagnosticImporter.Aurora, "broken") { File = "core/z.xml" };
        var blocking = new ImportDiagnostic(ImportDiagnosticSeverity.Error, ImportDiagnosticCode.InvalidValue, ImportDiagnosticImporter.Homebrew, "no name");

        var arranged = ImportDiagnostic.Arrange(grants.Reverse().Append(unreadable).Prepend(blocking).Append(blocking));

        arranged.Should().HaveCount(52, "one error (twice the same is once), one unreadable file, fifty grants");
        arranged[0].Should().Be(blocking, "the most serious first");
        arranged[1].Code.Should().Be(ImportDiagnosticCode.UnreadableFile, "then by code, in the order the codes are declared");
        arranged.Skip(2).Select(d => d.ElementId).Should().Equal(Enumerable.Range(0, 50).Select(i => $"ID_{i:000}"));
        arranged[^1].Omitted.Should().Be(70);
        arranged.Take(51).Should().OnlyContain(d => d.Omitted == null);
    }
}
