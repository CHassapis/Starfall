using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Starlights.Modules.Characters.Data;
using Starlights.Modules.Characters.Domain.Characters;
using Starlights.Modules.Characters.Services.Calculation;
using Starlights.Modules.Characters.Services.Magic;
using Starlights.Modules.Characters.Services.Statistics;
using Starlights.Modules.Elements.Integration;
using Starlights.Platform.Data;

namespace Starlights.Modules.Characters.Endpoints.Characters.Calculation;

public sealed record GetCalculationTraceRequest
{
    /// <summary>The value to trace: "skill:athletics", "ability-score:strength", "statistic:ac:misc" (see docs/api/README.md).</summary>
    [QueryParam]
    public string? Value { get; init; }

    /// <summary>The contract version the caller understands; 1 when omitted.</summary>
    [QueryParam]
    public int? Version { get; init; }
}

/// <summary>
/// How one of a character's figures was worked out: the same calculation the statistics, skills and spellcasting
/// endpoints run, with a recorder, so the trace's final value is the figure itself. Read only: the character is not
/// saved (the calculation changes the loaded character in memory, and nothing here writes it back). Under
/// /api/characters/{id}, so a password-locked player's character needs that player's token, like every other
/// endpoint of the character.
/// </summary>
public sealed class GetCalculationTraceEndpoint : Endpoint<GetCalculationTraceRequest, CalculationTraceResponse>
{
    private readonly IPersistence _persistence;
    private readonly StatisticsCalculator _calculator;
    private readonly ISpellIndex _spells;
    private readonly IElementsModuleQueries _elements;

    public GetCalculationTraceEndpoint(IPersistence persistence, StatisticsCalculator calculator, ISpellIndex spells, IElementsModuleQueries elements)
    {
        _persistence = persistence;
        _calculator = calculator;
        _spells = spells;
        _elements = elements;
    }

    public override void Configure()
    {
        Get("{characterId:guid}/calculation/trace");
        Group<CharactersGroup>();
        AllowAnonymous();
        Description(b => b.ClearDefaultProduces()
            .Produces<CalculationTraceResponse>(StatusCodes.Status200OK, "application/json")
            .Produces<CalculationTraceError>(StatusCodes.Status400BadRequest, "application/json")
            .Produces<CalculationTraceError>(StatusCodes.Status404NotFound, "application/json"));
    }

    public override async Task HandleAsync(GetCalculationTraceRequest req, CancellationToken ct)
    {
        if (req.Version is { } version && version != CalculationTraceContract.Version)
        {
            await Refuse(StatusCodes.Status400BadRequest, "unsupported-version", $"This server has calculation trace contract version {CalculationTraceContract.Version}, not {version}.", ct);
            return;
        }
        var key = req.Value?.Trim() ?? "";
        if (key.Length == 0 || key.Length > 220 || !(CalculationTraceKeys.IsServerKey(key) || CalculationTraceKeys.IsWebKey(key)))
        {
            await Refuse(StatusCodes.Status400BadRequest, "invalid-argument",
                key.Length == 0 ? "Say which value to trace: ?value=skill:athletics" : $"\"{(key.Length > 60 ? key[..60] + "…" : key)}\" is not a value that can be traced.", ct);
            return;
        }

        var characterId = new CharacterId(Route<Guid>("characterId"));
        var character = await _persistence.GetRepository<ICharactersRepository>().GetCharacterAsync(characterId);
        if (character is null)
        {
            await Refuse(StatusCodes.Status404NotFound, "not-found", "There is no such character.", ct);
            return;
        }
        if (!CalculationTraceKeys.IsServerKey(key))
        {
            await Send.OkAsync(new CalculationTraceResponse { CharacterId = characterId.Value, Value = key, Status = TraceStatus.Unsupported, Reason = "The web app works this value out and traces it itself." }, ct);
            return;
        }

        var registrations = await _persistence.GetRepository<IRegistrationRepository>().GetRegistrationsAsync(characterId);
        var recorder = new StatisticsTraceRecorder();
        var statistics = _calculator.Calculate(character, registrations, recorder).Statistics;

        SpellcastingFactsModel? spellcasting = null;
        if (key.StartsWith("spell-", StringComparison.Ordinal))
        {
            var input = MagicCharacterFactory.Create(character, registrations, statistics);
            var magic = await _spells.GetElementMagicAsync(input.Registrations.Select(r => r.ElementId).Distinct().ToList(), ct);
            spellcasting = SpellcastingFacts.Build(input, magic, await _spells.GetAsync(ct));
        }

        var builder = new CalculationTraceBuilder(recorder, statistics, registrations, spellcasting);
        var (status, trace, reason) = builder.Build(key);
        CalculationTraceContent? content = null;
        if (trace is not null)
        {
            var rules = builder.RulesIn(trace);
            var elements = (await _elements.GetElementsWithRules(rules.Select(r => builder.RegistrationOf(r)?.AssociatedElementId.Value ?? Guid.Empty).Distinct().ToList()))
                .ToDictionary(e => e.Id);
            var used = rules.Select(r =>
            {
                var element = builder.RegistrationOf(r) is { } registration ? elements.GetValueOrDefault(registration.AssociatedElementId.Value) : null;
                // the server's own elements (abilities, skills) have no book: they say nothing of the content
                return (Rule: r, Element: element?.Name ?? builder.RegistrationOf(r)?.AssociatedElementName ?? "", Source: element is { BuiltIn: false } ? element.Source : null);
            }).ToList();
            var sources = used.Select(u => u.Source).OfType<string>().Where(s => s.Length > 0).Distinct().Order(StringComparer.Ordinal).ToList();
            content = new CalculationTraceContent
            {
                Sources = sources,
                Editions = sources.Select(s => s.Contains("2024", StringComparison.Ordinal) ? "2024" : "2014").Distinct().Order(StringComparer.Ordinal).ToList(),
                Fingerprint = CalculationTraceBuilder.Fingerprint(used),
            };
        }

        // nothing is saved: the calculation's changes to the loaded character stay in this request
        await Send.OkAsync(new CalculationTraceResponse { CharacterId = characterId.Value, Value = key, Status = status, Trace = trace, Content = content, Reason = reason }, ct);
    }

    private async Task Refuse(int status, string code, string message, CancellationToken ct)
    {
        HttpContext.Response.StatusCode = status;
        await HttpContext.Response.WriteAsJsonAsync(new CalculationTraceError { Code = code, Message = message }, ct);
    }
}
