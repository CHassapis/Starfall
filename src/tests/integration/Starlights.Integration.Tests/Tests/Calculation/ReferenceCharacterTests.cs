using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Starlights.Modules.Characters.Data.EntityFramework;
using Starlights.Modules.Characters.Services.Calculation;
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
        var spec = JsonNode.Parse(await File.ReadAllTextAsync(ReferenceFile("characters", $"{character}.json"), TestContext.CancellationToken))!;
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
        failures.AddRange(await CheckTracesAsync(builder, api, expected, "built"));

        foreach (var scenario in spec["scenarios"]?.AsArray() ?? [])
        {
            foreach (var step in scenario!["steps"]!.AsArray())
            {
                await builder.ApplyAsync(step!);
            }
            expected = Merge(expected, (JsonObject)scenario["expected"]!);
            failures.AddRange(await CheckAsync(builder, expected, scenario["id"]!.GetValue<string>()));
            failures.AddRange(await CheckTracesAsync(builder, api, expected, scenario["id"]!.GetValue<string>()));
        }

        failures.Should().BeEmpty();
    }

    [TestMethod]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task TheItemCatalog_DescribesTheReferenceItemsAsTheWebTestsDo()
    {
        var catalog = JsonNode.Parse(await File.ReadAllTextAsync(ReferenceFile("catalog.json"), TestContext.CancellationToken))!["items"]!.AsArray();
        await using var host = await StartAsync();
        using var api = host.Integration.CreateClient();
        var served = JsonNode.Parse(await api.GetStringAsync("/api/elements/items", TestContext.CancellationToken))!["items"]!.AsArray()
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

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task TracingACharacter_ChangesNothingInTheDatabase()
    {
        // the wizard: an item sets Strength, items stack and do not; the calculation changes the loaded character
        var spec = JsonNode.Parse(await File.ReadAllTextAsync(ReferenceFile("characters", "c05-wizard-2014.json"), TestContext.CancellationToken))!;
        await using var host = await StartAsync();
        using var api = host.Integration.CreateClient();
        var builder = new ReferenceCharacterBuilder(host.Integration, api);
        await builder.CreateAsync("No mutation");
        foreach (var step in spec["steps"]!.AsArray())
        {
            await builder.ApplyAsync(step!);
        }

        // a stale score, as if the character had not been worked out since: a calculation that was saved would fix it
        await host.ExecuteAsync("UPDATE characters.ability_scores SET additional_score = additional_score + 3, calculated_score = calculated_score + 3 WHERE name = 'Strength'");
        var before = await host.SnapshotAsync();

        foreach (var key in new[] { "ability-score:strength", "skill:athletics", "saving-throw:wisdom", "statistic:ac:misc", "spell-attack:ref-wizard", "proficiency-bonus", "armor-class", "statistic:nothing" })
        {
            (await api.GetAsync($"/api/characters/{builder.CharacterId}/calculation/trace?value={Uri.EscapeDataString(key)}", TestContext.CancellationToken)).IsSuccessStatusCode.Should().BeTrue(key);
        }

        (await host.SnapshotAsync()).Should().Equal(before, "tracing writes nothing: no row and no event");

        // and the check would see a write: the statistics endpoint saves its calculation, which puts the score right
        (await api.GetAsync($"/api/characters/{builder.CharacterId}/statistics", TestContext.CancellationToken)).EnsureSuccessStatusCode();
        (await host.SnapshotAsync()).Should().NotEqual(before);
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

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The server's traces: every one comes to the figure the other endpoints give (ability scores and modifiers,
    /// saving throws, skills, the proficiency bonus, every statistic, each spellcasting's attack and DC), every step
    /// replays to its own trace's value, and the character file's traces have their final value, exclusions and
    /// operations.
    /// </summary>
    private static async Task<List<string>> CheckTracesAsync(ReferenceCharacterBuilder builder, HttpClient api, JsonObject expected, string stage)
    {
        var failures = new List<string>();
        var traced = new Dictionary<string, CalculationTraceResponse>();
        async Task<CalculationTrace?> Trace(string key)
        {
            if (!traced.TryGetValue(key, out var response))
            {
                var http = await api.GetAsync($"/api/characters/{builder.CharacterId}/calculation/trace?value={Uri.EscapeDataString(key)}");
                if (!http.IsSuccessStatusCode)
                {
                    failures.Add($"[{stage}] trace {key}: {(int)http.StatusCode} {await http.Content.ReadAsStringAsync()}");
                    return null;
                }
                var body = await http.Content.ReadAsStringAsync();
                traced[key] = response = JsonSerializer.Deserialize<CalculationTraceResponse>(body, Web)!;
                // the worked example in the API docs is a real trace:
                // STARLIGHTS_WRITE_TRACE_EXAMPLE=<repository>/docs/api/examples/calculation-trace.json
                if (key == "spell-attack:ref-bard" && stage == "built" && Environment.GetEnvironmentVariable("STARLIGHTS_WRITE_TRACE_EXAMPLE") is { Length: > 0 } example)
                {
                    await File.WriteAllTextAsync(example, JsonNode.Parse(body)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
                }
            }
            if (response.Status != TraceStatus.Traced)
            {
                failures.Add($"[{stage}] trace {key}: {response.Status} ({response.Reason})");
                return null;
            }
            foreach (var t in All(response.Trace!))
            {
                if (t.Notes.Count > 0 || CalculationTraceBuilder.Replay(t.Steps) != t.Final)
                {
                    failures.Add($"[{stage}] trace {key}: {t.Key} replays to {CalculationTraceBuilder.Replay(t.Steps)}, not {t.Final} {string.Join(" ", t.Notes)}");
                }
            }
            return response.Trace;
        }
        async Task Same(string key, int figure, string what)
        {
            if (await Trace(key) is { } t && t.Final != figure)
            {
                failures.Add($"[{stage}] trace {key} comes to {t.Final}, {what} is {figure}");
            }
        }

        foreach (var a in (await builder.GetCharacterAsync("/ability-scores"))["abilityScores"]!.AsArray())
        {
            var ability = a!["name"]!.GetValue<string>().ToLowerInvariant();
            await Same($"ability-score:{ability}", a["calculatedScore"]!.GetValue<int>(), "the ability score");
            await Same($"ability-modifier:{ability}", a["calculatedModifier"]!.GetValue<int>(), "the ability modifier");
        }
        var abilities = new Dictionary<string, string> { ["STR"] = "strength", ["DEX"] = "dexterity", ["CON"] = "constitution", ["INT"] = "intelligence", ["WIS"] = "wisdom", ["CHA"] = "charisma" };
        foreach (var save in (await builder.GetCharacterAsync("/saving-throws"))["savingThrows"]!.AsArray())
        {
            await Same($"saving-throw:{abilities[save!["abilityScoreAbbreviation"]!.GetValue<string>()]}", save["calculatedBonus"]!.GetValue<int>(), "the saving throw");
        }
        foreach (var skill in (await builder.GetCharacterAsync("/skills"))["skills"]!.AsArray())
        {
            await Same($"skill:{skill!["name"]!.GetValue<string>().ToLowerInvariant().Replace(' ', '-')}", skill["calculatedBonus"]!.GetValue<int>(), "the skill");
        }
        foreach (var statistic in (await builder.GetCharacterAsync("/statistics"))["statistics"]!.AsArray())
        {
            var name = statistic!["groupName"]!.GetValue<string>();
            await Same(name == "proficiency" ? "proficiency-bonus" : $"statistic:{name}", statistic["totalValue"]!.GetValue<int>(), $"statistic {name}");
        }
        foreach (var caster in (await builder.GetCharacterAsync("/spellcasting"))["casters"]!.AsArray())
        {
            var slug = caster!["name"]!.GetValue<string>().Trim().ToLowerInvariant().Replace(' ', '-');
            await Same($"spell-attack:{slug}", caster["attack"]!.GetValue<int>(), "the spell attack");
            await Same($"spell-save-dc:{slug}", caster["dc"]!.GetValue<int>(), "the spell save DC");
        }

        // the character file's own trace expectations (the sheet's keys are the web suite's)
        foreach (var want in expected["traces"]?.AsArray() ?? [])
        {
            var key = want!["value"]!.GetValue<string>();
            if (!CalculationTraceKeys.IsServerKey(key) || await Trace(key) is not { } t)
            {
                continue;
            }
            if (t.Final != want["final"]!.GetValue<int>())
            {
                failures.Add($"[{stage}] trace {key}: final {t.Final}, expected {want["final"]}");
            }
            var excluded = string.Join(", ", t.Excluded.Select(e => $"{JsonSerializer.Serialize(e.Reason).Trim('"')} {e.Value}").Order(StringComparer.Ordinal));
            var wantExcluded = string.Join(", ", want["excluded"]!.AsArray().Select(e => $"{e!["reason"]} {e["value"]}").Order(StringComparer.Ordinal));
            if (excluded != wantExcluded)
            {
                failures.Add($"[{stage}] trace {key}: excluded [{excluded}], expected [{wantExcluded}]");
            }
            var operations = All(t).SelectMany(x => x.Steps).Select(x => JsonSerializer.Serialize(x.Operation).Trim('"')).ToHashSet();
            foreach (var operation in want["operations"]?.AsArray().Select(o => o!.GetValue<string>()) ?? [])
            {
                if (!operations.Contains(operation))
                {
                    failures.Add($"[{stage}] trace {key}: no {operation} step (it has {string.Join(", ", operations)})");
                }
            }
        }
        return failures;
    }

    private static IEnumerable<CalculationTrace> All(CalculationTrace trace) =>
        new[] { trace }.Concat(trace.Steps.Where(s => s.Input is not null).SelectMany(s => All(s.Input!)));

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

        public async Task ExecuteAsync(string sql)
        {
            await using var connected = new SqlConnection(connection);
            await connected.OpenAsync();
            await using var command = new SqlCommand(sql, connected);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Every table of the characters schema: its row count and a checksum of its rows.</summary>
        public async Task<List<string>> SnapshotAsync()
        {
            await using var connected = new SqlConnection(connection);
            await connected.OpenAsync();
            var tables = new List<string>();
            await using (var list = new SqlCommand("SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('characters') ORDER BY name", connected))
            await using (var reader = await list.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }
            var snapshot = new List<string>();
            foreach (var table in tables)
            {
                await using var sum = new SqlCommand($"SELECT COUNT(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM characters.[{table}]", connected);
                await using var reader = await sum.ExecuteReaderAsync();
                await reader.ReadAsync();
                snapshot.Add($"{table} {reader.GetInt32(0)} {(reader.IsDBNull(1) ? "-" : reader.GetInt32(1))}");
            }
            return snapshot;
        }

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
