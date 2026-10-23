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
/// Every choice of the real content offers something: the whole catalog (Aurora Legacy, its pull requests, homebrew,
/// 5etools, the built-in extras) imported the way the server does, and each choice's options worked out the way the
/// builder does (<see cref="SupportsExpression"/> over the elements of the choice's type). A choice with none is what
/// a player sees as "No options". Needs AURORA_ELEMENTS (and AURORA_PRS, STARLIGHTS_HOMEBREW, FIVETOOLS_DATA).
/// </summary>
[TestClass]
public class AuroraChoicesAuditTests
{
    public TestContext TestContext { get; set; } = default!;

    [TestMethod]
    public async Task EveryChoice_HasOptions()
    {
        var aurora = Environment.GetEnvironmentVariable("AURORA_ELEMENTS");
        if (string.IsNullOrEmpty(aurora) || !Directory.Exists(aurora))
        {
            Assert.Inconclusive("AURORA_ELEMENTS is not set to a clone of the Aurora Legacy elements");
        }

        var added = new Dictionary<Guid, Element>();
        var repository = new Mock<IElementsRepository>();
        repository.Setup(x => x.GetElementSummariesAsync()).ReturnsAsync([]);
        repository.Setup(x => x.Add(It.IsAny<Element>())).Callback<Element>(e => added.TryAdd(e.Id, e));
        var persistence = new Mock<IPersistence>();
        persistence.Setup(x => x.GetRepository<IElementsRepository>()).Returns(repository.Object);

        var importer = new AuroraImporter(NullLogger<AuroraImporter>.Instance, persistence.Object,
            new AuroraImporterOptions(aurora!, HomebrewPath: Environment.GetEnvironmentVariable("STARLIGHTS_HOMEBREW"), PullRequestsPath: Environment.GetEnvironmentVariable("AURORA_PRS")),
            new FiveEToolsData(new FiveEToolsOptions(Environment.GetEnvironmentVariable("FIVETOOLS_DATA") ?? "/nonexistent"), NullLogger<FiveEToolsData>.Instance));
        foreach (var index in new[] { "AuroraLegacy.index", AuroraImporterOptions.PullRequestsIndex, AuroraImporterOptions.HomebrewIndex, AuroraImporterOptions.FiveEToolsIndex, AuroraImporterOptions.BuiltInIndex })
        {
            await importer.ImportAsync(index, cancellationToken: TestContext.CancellationToken);
        }

        var byType = added.Values.GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.ToList());
        var none = new List<string>();
        var checkedChoices = 0;
        foreach (var element in added.Values)
        {
            foreach (var choice in element.GetComponents<SelectionRuleComponent>())
            {
                // spell choices follow the character's spell lists and slots ($(spellcasting:…)); those are the builder's
                if (choice.Supports?.Contains("$(", StringComparison.Ordinal) == true)
                {
                    continue;
                }
                checkedChoices++;
                var candidates = byType.GetValueOrDefault(choice.ElementType, []);
                var spell = choice.ElementType == "Spell";
                // spells are matched by the spell index: their lists, level, school and ritual, numbers being levels
                var matches = string.IsNullOrWhiteSpace(choice.Supports) ? null : (spell ? SupportsExpression.Compile(choice.Supports, numbersAreTerms: true) : SupportsExpression.Compile(choice.Supports));
                if (!candidates.Any(c => matches is null || matches(spell ? SpellTerms(c) : c.GetComponent<SupportsComponent>()?.Supports ?? [], c.GetComponent<AuroraSourceComponent>()?.AuroraId)))
                {
                    var source = element.GetComponent<AuroraSourceComponent>();
                    none.Add($"{source?.File} | {element.Name} ({element.Type}) | \"{choice.Name}\" {choice.ElementType}: {choice.Supports}");
                }
            }
        }

        Console.WriteLine($"{added.Count} elements, {checkedChoices} choices checked, {none.Count} with no options");
        foreach (var line in none.Order(StringComparer.Ordinal))
        {
            Console.WriteLine("  " + line);
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "choices-without-options.txt"), none.Order(StringComparer.Ordinal));
        none.Should().BeEmpty();
    }

    private static IReadOnlyCollection<string> SpellTerms(Element spell)
    {
        var xml = System.Xml.Linq.XElement.Parse(spell.GetComponent<AuroraSourceComponent>()?.RawXml ?? "<element/>");
        string? Set(string name) => xml.Element("setters")?.Elements("set").FirstOrDefault(s => (string?)s.Attribute("name") == name)?.Value.Trim();
        var terms = new List<string>(spell.GetComponent<SupportsComponent>()?.Supports ?? []);
        if (Set("level") is { } level)
        {
            terms.Add(level);
        }
        if (Set("school") is { } school)
        {
            terms.Add(school);
        }
        if (Set("isRitual") == "true")
        {
            terms.Add("Ritual");
        }
        return terms;
    }
}
