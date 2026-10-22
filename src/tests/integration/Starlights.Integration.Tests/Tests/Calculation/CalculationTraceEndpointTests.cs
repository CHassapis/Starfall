using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Starlights.Integration.Drivers.CharacterCreation;
using Starlights.Integration.Extensions;
using Starlights.Modules.Characters.Services.Calculation;

namespace Starlights.Integration.Tests.Calculation;

/// <summary>
/// The calculation trace endpoint's contract: what it answers for a value it traces, one the web app traces, one the
/// character does not have, and the requests it refuses, each with its stable code; and how the API description
/// names the contract's values. The reference character tests check the traces of real builds.
/// </summary>
[TestClass]
public sealed class CalculationTraceEndpointTests : IntegrationTestBase
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private IntegrationHost _integration = default!;
    private Guid _characterId;

    [TestInitialize]
    public async Task Initialize()
    {
        _integration = IntegrationHost.CreateDefaultBuilder(this).Build();
        await _integration.InitializeElements();
        _characterId = await _integration.GetDriver<CharacterCreationDriver>().CreateCharacterAsync();
    }

    [TestCleanup]
    public void Cleanup() => _integration.Dispose();

    private async Task<HttpResponseMessage> Trace(string query, Guid? characterId = null)
    {
        using var client = _integration.CreateClient();
        return await client.GetAsync($"/api/characters/{characterId ?? _characterId}/calculation/trace{query}", _integration.CancellationToken);
    }

    [TestMethod]
    public async Task AValueTheServerCalculates_IsTraced_ToTheFigureTheCharacterHas()
    {
        using var client = _integration.CreateClient();
        var scores = JsonNode.Parse(await client.GetStringAsync($"/api/characters/{_characterId}/ability-scores", _integration.CancellationToken))!["abilityScores"]!.AsArray();
        var strength = scores.First(a => a!["abbreviation"]!.GetValue<string>() == "STR")!["calculatedScore"]!.GetValue<int>();

        var response = await Trace("?value=ability-score:strength&version=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<CalculationTraceResponse>(Web, _integration.CancellationToken))!;
        body.ContractVersion.Should().Be(1);
        body.CharacterId.Should().Be(_characterId);
        body.Value.Should().Be("ability-score:strength");
        body.Status.Should().Be(TraceStatus.Traced);
        body.Trace!.Final.Should().Be(strength);
        body.Trace.Steps[0].Source!.Kind.Should().Be(TraceSourceKind.UserSupplied, "the base score is what the player entered");
        CalculationTraceBuilder.Replay(body.Trace.Steps).Should().Be(body.Trace.Final);
        body.Content!.Fingerprint.Should().StartWith("sha256:");

        // the contract's values are words on the wire
        var raw = JsonNode.Parse(await (await Trace("?value=ability-modifier:strength")).Content.ReadAsStringAsync())!;
        raw["status"]!.GetValue<string>().Should().Be("traced");
        raw["trace"]!["steps"]!.AsArray().Select(s => s!["operation"]!.GetValue<string>()).Should().Equal("start", "add", "divide", "round-down");
    }

    [TestMethod]
    public async Task AValueTheWebAppWorksOut_IsUnsupported_NotMadeUp()
    {
        var body = (await (await Trace("?value=armor-class")).Content.ReadFromJsonAsync<CalculationTraceResponse>(Web, _integration.CancellationToken))!;

        body.Status.Should().Be(TraceStatus.Unsupported);
        body.Trace.Should().BeNull();
        body.Reason.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task AValueTheCharacterDoesNotHave_IsMissing_WithTheReason()
    {
        foreach (var key in new[] { "statistic:ac:nothing-gives-this", "spell-attack:wizard" })
        {
            var body = (await (await Trace($"?value={key}")).Content.ReadFromJsonAsync<CalculationTraceResponse>(Web, _integration.CancellationToken))!;

            body.Status.Should().Be(TraceStatus.Missing, key);
            body.Trace.Should().BeNull();
            body.Reason.Should().NotBeNullOrEmpty();
        }
    }

    [TestMethod]
    [DataRow("", "invalid-argument")]
    [DataRow("?value=", "invalid-argument")]
    [DataRow("?value=DROP%20TABLE", "invalid-argument")]
    [DataRow("?value=ability-score:luck", "invalid-argument")]
    [DataRow("?value=skill:athletics&version=2", "unsupported-version")]
    public async Task ARequestThatCannotBeAnswered_IsRefused_WithAStableCode(string query, string code)
    {
        var response = await Trace(query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = (await response.Content.ReadFromJsonAsync<CalculationTraceError>(Web, _integration.CancellationToken))!;
        error.Code.Should().Be(code);
        error.ContractVersion.Should().Be(1);
        error.Message.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public async Task ACharacterThatDoesNotExist_IsNotFound()
    {
        var response = await Trace("?value=skill:athletics", Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<CalculationTraceError>(Web, _integration.CancellationToken))!.Code.Should().Be("not-found");
    }

    [TestMethod]
    public async Task TheApiDescription_NamesTheContractsValues()
    {
        using var client = _integration.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json", _integration.CancellationToken))!;
        var schemas = document["components"]!["schemas"]!;

        document["paths"]!["/api/characters/{characterId}/calculation/trace"]!["get"].Should().NotBeNull();
        schemas["TraceOperation"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>())
            .Should().Equal("start", "add", "replace", "maximum", "minimum", "multiply", "divide", "round-down", "round-up", "round-toward-zero");
        schemas["TraceExclusionReason"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>())
            .Should().Equal("stacking", "lower-alternative", "set-not-higher", "condition-not-met", "unresolved-dependency");
        schemas["TraceSourceKind"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>())
            .Should().Equal("rule", "user-supplied", "character", "fixed", "calculated");
        schemas["TraceStatus"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()).Should().Equal("traced", "missing", "unsupported");
        schemas["CalculationTraceError"]!["properties"]!["code"].Should().NotBeNull();
    }
}
