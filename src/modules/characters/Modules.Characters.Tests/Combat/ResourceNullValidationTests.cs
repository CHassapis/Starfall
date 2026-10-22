using AwesomeAssertions;
using Starlights.Modules.Characters.Domain.Characters;

namespace Starlights.Modules.Characters.Tests.Combat;

[TestClass]
public class ResourceNullValidationTests
{
    [TestMethod]
    public void MalformedResourceEntriesReturnValidationErrors()
    {
        var valid = new CharacterResource { Id = "r1", Name = "Test pool" };
        CharacterResource[] malformed = [null!, valid with { Id = null! }, valid with { Max = null! }];
        foreach (var entry in malformed)
        {
            new CharacterCombat { Resources = [entry] }.Validate().Should().NotBeNull();
        }
    }
}
