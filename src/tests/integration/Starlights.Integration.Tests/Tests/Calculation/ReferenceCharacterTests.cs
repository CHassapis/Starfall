using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Starlights.Modules.Characters.Data.EntityFramework;
using Starlights.Modules.Elements.Data.EntityFramework;

namespace Starlights.Integration.Tests.Calculation;

/// <summary>
/// The reference characters (src/frontend/apps/builder-app/reference-characters): synthetic content imported into an
/// empty database, each character built through the API, and the server's figures checked against the ones worked
/// out by hand in the character's file. The web tests check the sheet's own calculations against the same files.
/// </summary>
[TestClass]
[TestCategory("SqlServer")]
public sealed partial class ReferenceCharacterTests : IntegrationTestBase
{
    // the statistics the sheet, the PDF and the battle view read (lib/api/sheet.ts, lib/rules/items.ts): a reference
    // character lists every one it has, so the web tests can work from the file alone
    [GeneratedRegex(@"^(initiative|hp|attunement:max|barbarian-rage:damage|speed(:.+)?|innate-speed(:.+)?|ac:.+|[a-z0-9-]+:(attack|damage)|([a-z0-9-]+:)?spellcasting:(attack|dc))$")]
    private static partial Regex SheetStatistic();

    // registered elements the sheet's calculations look for: weapon proficiencies (attacks) and Extra Attack
    [GeneratedRegex(@"^ID_(REF_)?PROFICIENCY_WEAPON_")]
    private static partial Regex SheetElement();

    public static IEnumerable<object[]> Characters =>
        Directory.GetFiles(ReferenceFile("characters"), "*.json").Order(StringComparer.Ordinal).Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

    [TestMethod]
    [DynamicData(nameof(Characters))]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task TheCharacter_HasTheFiguresWorkedOutByHand(string character)
    {
        var spec = JsonNode.Parse(await File.ReadAllTextAsync(ReferenceFile("characters", $"{character}.json")))!;
        await using var host = await StartAsync();
        using var api = host.Integration.CreateClient();
        var builder = new ReferenceCharacterBuilder(host.Integration, api);
        await builder.CreateAsync(spec["title"]!.GetValue<string>());

        foreach (var step in spec["steps"]!.AsArray())
        {
            await builder.ApplyAsync(step!);
        }
        var expected = (JsonObject)spec["expected"]!.DeepClone();
        var failures = await CheckAsync(builder, expected, "built");

        foreach (var scenario in spec["scenarios"]?.AsArray() ?? [])
        {
            foreach (var step in scenario!["steps"]!.AsArray())
            {
                await builder.ApplyAsync(step!);
            }
            expected = Merge(expected, (JsonObject)scenario["expected"]!);
            failures.AddRange(await CheckAsync(builder, expected, scenario["id"]!.GetValue<string>()));
        }

        failures.Should().BeEmpty();
    }

    [TestMethod]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task TheItemCatalog_DescribesTheReferenceItemsAsTheWebTestsDo()
    {
        var catalog = JsonNode.Parse(await File.ReadAllTextAsync(ReferenceFile("catalog.json")))!["items"]!.AsArray();
        await using var host = await StartAsync();
        using var api = host.Integration.CreateClient();
        var served = JsonNode.Parse(await api.GetStringAsync("/api/elements/items"))!["items"]!.AsArray()
            .Where(i => i!["auroraId"]?.GetValue<string>().StartsWith("ID_REF_", StringComparison.Ordinal) == true)
            .ToDictionary(i => i!["auroraId"]!.GetValue<string>(), i => i!);

        var failures = new List<string>();
        foreach (var item in catalog)
        {
            var auroraId = item!["auroraId"]!.GetValue<string>();
            if (!served.TryGetValue(auroraId, out var server))
            {
                failures.Add($"{auroraId} is not in the item catalog");
                continue;
            }
            foreach (var field in new[] { "name", "elementType", "slot", "hasRules", "weapon", "armor", "magic", "base" })
            {
                var want = Comparable(item[field]);
                var have = Comparable(server[field]);
                if (want != have)
                {
                    failures.Add($"{auroraId} {field}: catalog.json has {want}, the server {have}");
                }
            }
        }
        served.Keys.Except(catalog.Select(i => i!["auroraId"]!.GetValue<string>())).Should().BeEmpty("catalog.json lists every reference item");
        failures.Should().BeEmpty();
    }

    // ---- checks

    private static async Task<List<string>> CheckAsync(ReferenceCharacterBuilder builder, JsonObject expected, string stage)
    {
        var failures = new List<string>();
        void Expect(string what, object? want, object? have)
        {
            if (!Equals(want?.ToString(), have?.ToString()))
            {
                failures.Add($"[{stage}] {what}: expected {want ?? "nothing"}, the server has {have ?? "nothing"}");
            }
        }

        var character = (await builder.GetCharacterAsync())["character"]!;
        if (expected["level"] is not null)
        {
            Expect("level", expected["level"]!.GetValue<int>(), character["level"]!.GetValue<int>());
        }

        var statistics = (await builder.GetCharacterAsync("/statistics"))["statistics"]!.AsArray()
            .ToDictionary(s => s!["groupName"]!.GetValue<string>(), s => s!["totalValue"]!.GetValue<int>());
        if (expected["proficiencyBonus"] is not null)
        {
            Expect("proficiency bonus", expected["proficiencyBonus"]!["value"]!.GetValue<int>(), statistics.GetValueOrDefault("proficiency"));
        }

        var abilities = (await builder.GetCharacterAsync("/ability-scores"))["abilityScores"]!.AsArray();
        foreach (var (abbreviation, want) in expected["abilities"]?.AsObject() ?? [])
        {
            var have = abilities.First(a => a!["abbreviation"]!.GetValue<string>() == abbreviation)!;
            Expect($"{abbreviation} score", want!["score"]!.GetValue<int>(), have["calculatedScore"]!.GetValue<int>());
            Expect($"{abbreviation} modifier", want["modifier"]!.GetValue<int>(), have["calculatedModifier"]!.GetValue<int>());
        }

        var saves = (await builder.GetCharacterAsync("/saving-throws"))["savingThrows"]!.AsArray();
        foreach (var (abbreviation, want) in expected["saves"]?.AsObject() ?? [])
        {
            var have = saves.First(s => s!["abilityScoreAbbreviation"]?.GetValue<string>() == abbreviation)!;
            Expect($"{abbreviation} saving throw", want!["value"]!.GetValue<int>(), have["calculatedBonus"]!.GetValue<int>());
        }

        var skills = (await builder.GetCharacterAsync("/skills"))["skills"]!.AsArray();
        foreach (var (name, want) in expected["skills"]?.AsObject() ?? [])
        {
            var have = skills.FirstOrDefault(s => s!["name"]!.GetValue<string>() == name);
            Expect($"{name}", want!["value"]!.GetValue<int>(), have?["calculatedBonus"]!.GetValue<int>());
        }

        var listed = expected["statistics"]?.AsObject() ?? [];
        foreach (var (name, want) in listed)
        {
            Expect($"statistic {name}", want!["value"]!.GetValue<int>(), statistics.TryGetValue(name, out var v) ? v : null);
        }
        foreach (var name in statistics.Keys.Where(n => SheetStatistic().IsMatch(n) && !listed.ContainsKey(n)))
        {
            failures.Add($"[{stage}] statistic {name} ({statistics[name]}) is one the sheet reads, but the reference character does not list it");
        }
        foreach (var name in expected["absentStatistics"]?.AsArray().Select(n => n!.GetValue<string>()) ?? [])
        {
            Expect($"statistic {name}", "absent", statistics.TryGetValue(name, out var v) ? v : "absent");
        }

        // registered elements, by their Aurora ids
        var registrations = Flatten((await builder.GetCharacterAsync("/registrations"))["registrations"]!.AsArray()).ToList();
        var entries = await builder.EntriesAsync(registrations.Select(r => r["associatedElementId"]!.GetValue<Guid>()));
        string? AuroraId(JsonNode r) => entries.GetValueOrDefault(r["associatedElementId"]!.GetValue<Guid>())?["auroraId"]?.GetValue<string>();
        var registered = registrations.Select(AuroraId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var wantRegistered = expected["registered"]?.AsArray().Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
        foreach (var id in wantRegistered.Where(id => !registered.Contains(id)))
        {
            failures.Add($"[{stage}] {id} is not registered");
        }
        foreach (var id in expected["notRegistered"]?.AsArray().Select(n => n!.GetValue<string>()) ?? [])
        {
            if (registered.Contains(id))
            {
                failures.Add($"[{stage}] {id} is registered");
            }
        }
        foreach (var r in registrations.Where(r => SheetElement().IsMatch(AuroraId(r) ?? "") || r["name"]!.GetValue<string>().EndsWith("Extra Attack", StringComparison.Ordinal)))
        {
            if (!wantRegistered.Contains(AuroraId(r) ?? ""))
            {
                failures.Add($"[{stage}] {AuroraId(r)} ({r["name"]}) is registered and the sheet looks for it, but the reference character does not list it");
            }
        }

        // classes with their hit dice
        if (expected["classes"] is JsonArray wantClasses)
        {
            var classes = (await builder.GetCharacterAsync("/classes"))["classes"]!.AsArray();
            var have = classes.Select(c =>
            {
                var registration = registrations.FirstOrDefault(r => r["registrationId"]!.GetValue<Guid>() == c!["registrationId"]!.GetValue<Guid>());
                var hd = registration is null ? null : entries.GetValueOrDefault(registration["associatedElementId"]!.GetValue<Guid>())?["setters"]?["hd"]?.GetValue<string>();
                return $"{c!["name"]} {c["level"]} d{hd?.TrimStart('d')}";
            }).Order(StringComparer.Ordinal);
            var want = wantClasses.Select(c => $"{c!["name"]} {c["level"]} d{c["hitDie"]}").Order(StringComparer.Ordinal);
            Expect("classes", string.Join(", ", want), string.Join(", ", have));
        }

        // spellcasting: the figures the server works out for each spellcasting
        var casters = (await builder.GetCharacterAsync("/spellcasting"))["casters"]!.AsArray();
        var wantCasting = expected["spellcasting"]?.AsObject() ?? [];
        Expect("spellcastings", string.Join(", ", wantCasting.Select(c => c.Key).Order(StringComparer.Ordinal)),
            string.Join(", ", casters.Select(c => c!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal)));
        foreach (var (name, want) in wantCasting)
        {
            var have = casters.FirstOrDefault(c => c!["name"]!.GetValue<string>() == name);
            if (have is null)
            {
                continue;
            }
            foreach (var field in new[] { "ability", "attack", "dc", "multiclass", "classLevel", "edition", "slots" })
            {
                if (want![field] is not null)
                {
                    Expect($"{name} {field}", Comparable(want[field]), Comparable(have[field]));
                }
            }
        }

        return failures;
    }

    private static IEnumerable<JsonNode> Flatten(JsonArray registrations) =>
        registrations.SelectMany(r => new[] { r! }.Concat(r!["children"] is JsonArray children ? Flatten(children) : []));

    /// <summary>
    /// A scenario's expectations over the ones before it: a scenario lists what it changes. Abilities, saving throws,
    /// skills, statistics and spellcastings are merged one by one; anything else is replaced.
    /// </summary>
    private static JsonObject Merge(JsonObject before, JsonObject scenario)
    {
        var merged = (JsonObject)before.DeepClone();
        foreach (var (key, value) in scenario)
        {
            if (key is "abilities" or "saves" or "skills" or "statistics" or "spellcasting" && merged[key] is JsonObject into)
            {
                foreach (var (name, entry) in value!.AsObject())
                {
                    into[name] = entry!.DeepClone();
                }
            }
            else
            {
                merged[key] = value?.DeepClone();
            }
        }
        // a statistic a scenario says is gone is no longer expected, and one it lists is no longer absent
        foreach (var name in scenario["absentStatistics"]?.AsArray().Select(n => n!.GetValue<string>()) ?? [])
        {
            merged["statistics"]?.AsObject().Remove(name);
        }
        if (scenario["absentStatistics"] is null && scenario["statistics"] is JsonObject listed && merged["absentStatistics"] is JsonArray absent)
        {
            merged["absentStatistics"] = new JsonArray(absent.Select(n => n!.GetValue<string>()).Where(n => !listed.ContainsKey(n)).Select(n => (JsonNode)n).ToArray());
        }
        return merged;
    }

    /// <summary>JSON for comparing: object keys in order, so the same content compares equal.</summary>
    private static string? Comparable(JsonNode? node) => node switch
    {
        null => null,
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"\"{p.Key}\":{Comparable(p.Value) ?? "null"}")) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(x => Comparable(x) ?? "null")) + "]",
        _ => node.ToJsonString(),
    };

    // ---- the host with the reference content

    /// <summary>
    /// A host on a new database of a disposable SQL Server (saving an inventory takes a row lock, which the
    /// in-memory provider cannot), with the reference content imported. Like the other SqlServer tests it needs
    /// <c>STARFALL_TEST_SQLSERVER</c>; without it the tests are inconclusive, unless <c>STARFALL_REQUIRE_SQLSERVER</c>
    /// is set (CI), where they fail.
    /// </summary>
    private async Task<ReferenceHost> StartAsync()
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

        var connection = new SqlConnectionStringBuilder(server) { InitialCatalog = $"starfall_ref_{Guid.NewGuid():N}"[..28] }.ConnectionString;
        await using (var elements = new ElementsContext(new DbContextOptionsBuilder<ElementsContext>().UseSqlServer(connection).Options))
        {
            await elements.Database.MigrateAsync();
        }
        await using (var characters = new CharactersContext(new DbContextOptionsBuilder<CharactersContext>().UseSqlServer(connection).Options))
        {
            await characters.Database.MigrateAsync();
        }

        var root = Directory.CreateTempSubdirectory("starfall-reference").FullName;
        var content = Directory.CreateDirectory(Path.Combine(root, "content")).FullName;
        foreach (var file in Directory.GetFiles(ReferenceFile("content")))
        {
            File.Copy(file, Path.Combine(content, Path.GetFileName(file)));
        }
        var integration = IntegrationHost.CreateDefaultBuilder(this)
            .ConfigureOptions(o =>
            {
                o.TestTimeout = TimeSpan.FromMinutes(10);
                o.Settings["Integration:SqlServer"] = connection;
                o.Settings["Aurora:ContentPath"] = content;
                o.Settings["Aurora:HomebrewPath"] = Path.Combine(root, "homebrew");
                o.Settings["Aurora:PullRequestsPath"] = Path.Combine(root, "no-pull-requests");
                o.Settings["FiveETools:Path"] = Path.Combine(root, "no-5etools");
            })
            .Build();
        var host = new ReferenceHost(integration, connection, root);
        try
        {
            await integration.InitializeElements();
            using var admin = integration.CreateAdminClient();
            var import = await admin.PostAsJsonAsync("/api/elements/import-aurora", new { Index = "reference.index", Replace = false, Update = false });
            var report = await import.Content.ReadAsStringAsync();
            import.IsSuccessStatusCode.Should().BeTrue(report);
            JsonNode.Parse(report)!["warnings"]!.AsArray().Should().BeEmpty("the reference content imports cleanly: {0}", report);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private sealed class ReferenceHost(IntegrationHost integration, string connection, string root) : IAsyncDisposable
    {
        public IntegrationHost Integration => integration;

        public async ValueTask DisposeAsync()
        {
            integration.Dispose();
            // as HomebrewSqlServerTests: not EnsureDeleted, which clears every connection pool
            SqlConnection.ClearPool(new SqlConnection(connection));
            await using var master = new SqlConnection(new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" }.ConnectionString);
            await master.OpenAsync();
            var database = new SqlConnectionStringBuilder(connection).InitialCatalog;
            await using var drop = new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];", master);
            await drop.ExecuteNonQueryAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    internal static string ReferenceFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Starlights.slnx")))
            {
                return Path.Combine([dir.FullName, "src", "frontend", "apps", "builder-app", "reference-characters", .. parts]);
            }
        }
        throw new InvalidOperationException("Can't find the repository root (Starlights.slnx).");
    }
}
