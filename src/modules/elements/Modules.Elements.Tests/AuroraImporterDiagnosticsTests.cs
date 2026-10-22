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

    private async Task<AuroraImportResult> ImportAsync(string index)
    {
        var repository = new Mock<IElementsRepository>();
        repository.Setup(x => x.GetElementSummariesAsync()).ReturnsAsync([]);
        var persistence = new Mock<IPersistence>();
        persistence.Setup(x => x.GetRepository<IElementsRepository>()).Returns(repository.Object);
        var importer = new AuroraImporter(
            NullLogger<AuroraImporter>.Instance,
            persistence.Object,
            new AuroraImporterOptions(Path.Combine(_root, "content"), HomebrewPath: Path.Combine(_root, "homebrew")),
            new FiveEToolsData(new FiveEToolsOptions(Path.Combine(_root, "no-5etools")), NullLogger<FiveEToolsData>.Instance));
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
        Homebrew("fine.xml", """<elements><element name="Fine" type="Feat" source="Brew" id="ID_BREW_FINE" /></elements>""");

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
