using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Starlights.Modules.Elements.Data;
using Starlights.Modules.Elements.Domain;
using Starlights.Modules.Elements.Domain.Components;
using Starlights.Modules.Elements.Services;
using Starlights.Modules.Elements.Services.FiveETools;
using Starlights.Platform.Data;

namespace Starlights.Modules.Elements.Tests;

/// <summary>
/// The items of a list select (a background's personality traits) become elements whose Aurora ids the web app's
/// character import builds from an Aurora file's list picks (aurora-character-import.ts, auroraListItemId): the two
/// have to agree on "list:{owner}:{list name}:{item id}".
/// </summary>
[TestClass]
public class AuroraListItemTests
{
    public TestContext TestContext { get; set; } = default!;

    private const string Background = """
        <elements>
          <element name="Test Background" type="Background" source="Test" id="ID_TEST_BACKGROUND">
            <rules>
              <select type="List" name="Personality Trait" number="2">
                <item id="5">Test trait five.</item>
                <item id="6">Test trait six.</item>
              </select>
              <select type="List" name="Ideal">
                <item>Test ideal.</item>
              </select>
            </rules>
          </element>
        </elements>
        """;

    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Directory.CreateTempSubdirectory("aurora-list-tests").FullName;
        Directory.CreateDirectory(Path.Combine(_root, "content"));
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "homebrew")).FullName, "background.xml"), Background);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    [TestMethod]
    public async Task ListItems_AreKeyedByOwner_ListName_AndItemId()
    {
        var added = new List<Element>();
        var repository = new Mock<IElementsRepository>();
        repository.Setup(x => x.GetElementSummariesAsync()).ReturnsAsync([]);
        repository.Setup(x => x.Add(It.IsAny<Element>())).Callback<Element>(added.Add);
        var persistence = new Mock<IPersistence>();
        persistence.Setup(x => x.GetRepository<IElementsRepository>()).Returns(repository.Object);
        var importer = new AuroraImporter(
            NullLogger<AuroraImporter>.Instance,
            persistence.Object,
            new AuroraImporterOptions(Path.Combine(_root, "content"), HomebrewPath: Path.Combine(_root, "homebrew")),
            new FiveEToolsData(new FiveEToolsOptions(Path.Combine(_root, "no-5etools")), NullLogger<FiveEToolsData>.Instance));

        await importer.ImportAsync(AuroraImporterOptions.HomebrewIndex, cancellationToken: TestContext.CancellationToken);

        var ids = added.Select(e => e.GetRequiredComponent<AuroraSourceComponent>().AuroraId).ToList();
        ids.Should().Contain("list:ID_TEST_BACKGROUND:Personality-Trait:5");
        ids.Should().Contain("list:ID_TEST_BACKGROUND:Personality-Trait:6");
        // an item without an id is numbered by its place in the list
        ids.Should().Contain("list:ID_TEST_BACKGROUND:Ideal:1");
    }
}
