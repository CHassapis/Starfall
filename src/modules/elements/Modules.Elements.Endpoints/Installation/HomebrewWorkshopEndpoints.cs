using System.Text.RegularExpressions;
using System.Xml.Linq;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Starlights.Modules.Elements.Data;
using Starlights.Modules.Elements.Integration;
using Starlights.Modules.Elements.Domain.Homebrew;
using Starlights.Platform.Data;

namespace Starlights.Modules.Elements.Endpoints.Installation;

/// <summary>
/// The Homebrew page's spells, subclasses and classes, kept as Aurora elements in their own files in the homebrew
/// folder (one per kind) and imported like uploaded homebrew. Reading is open; writing needs the admin key or token
/// (every non-GET under /api/elements, see Program.cs).
/// </summary>
internal static partial class HomebrewWorkshop
{
    public const string SpellsFile = "starlights-spells.xml";
    public const string SubclassesFile = "starlights-subclasses.xml";
    public const string ClassesFile = "starlights-classes.xml";

    [GeneratedRegex(@"^[\p{L}0-9 '’()\-]{1,50}$")]
    public static partial Regex ListName();

    public static string PathOf(IConfiguration config, string file) => Path.Combine(HomebrewFiles.Folder(config), file);

    public static XDocument Load(IConfiguration config, string file) =>
        File.Exists(PathOf(config, file)) ? XDocument.Load(PathOf(config, file)) : new XDocument(new XElement("elements"));

    /// <summary>The file as saved, read while no save writes it (a preview's reading).</summary>
    public static async Task<XDocument> ReadAsync(IConfiguration config, string file, CancellationToken ct)
    {
        await HomebrewContent.Gate.WaitAsync(ct);
        try
        {
            return Load(config, file);
        }
        finally
        {
            HomebrewContent.Gate.Release();
        }
    }

    public static IEnumerable<XElement> Mains(XDocument document, string type) =>
        document.Root!.Elements("element").Where(e => (string?)e.Attribute("type") == type);

    /// <summary>Puts a record (its main element and features) in place of the old one, saves and imports the file's kind.</summary>
    public static async Task SaveAsync(IConfiguration config, IAuroraImporter importer, string file, XDocument document, string id, IReadOnlyList<XElement>? record, CancellationToken ct)
    {
        var old = HomebrewWorkshopXml.RecordElements(document.Root!, id).ToList();
        var anchor = old.FirstOrDefault();
        if (record is not null)
        {
            if (anchor is null)
            {
                document.Root!.Add(record);
            }
            else
            {
                anchor.AddBeforeSelf(record);
            }
        }
        foreach (var e in old)
        {
            e.Remove();
        }
        Directory.CreateDirectory(HomebrewFiles.Folder(config));
        document.Save(PathOf(config, file));
        // the file's elements go (a removed feature with them), then what is left comes back
        await importer.RemoveAsync("homebrew/" + file, ct);
        await importer.ImportAsync(AuroraImporterIndex.Homebrew, replace: false, update: true, ct);
    }

    public static bool Short(string? text, int max) => (text?.Length ?? 0) <= max;

    /// <summary>The saved record's main element of a type, by id.</summary>
    public static XElement? Main(XDocument document, string type, string id) =>
        Mains(document, type).FirstOrDefault(e => (string?)e.Attribute("id") == id);

    /// <summary>The model of a record as the file has it after a save (what a read gives back).</summary>
    public static T Saved<T>(IConfiguration config, string file, string id, Func<XDocument, XElement, T> read) where T : class
    {
        var saved = Load(config, file);
        var main = saved.Root!.Elements("element").First(e => (string?)e.Attribute("id") == id);
        return read(saved, main);
    }

    /// <summary>A class's record as a save writes it: a renamed class keeps the keys its subclasses and spells use.</summary>
    public static (HomebrewClassModel Model, List<XElement> Record) ClassRecord(XDocument document, HomebrewClassModel req, string id)
    {
        if (Main(document, "Class", id) is { } existing)
        {
            var before = HomebrewWorkshopXml.ClassFromXml(document.Root!, existing);
            req = req with { SubclassSupports = req.SubclassSupports ?? before.SubclassSupports, SpellListName = req.SpellListName ?? before.SpellListName };
        }
        return (req, HomebrewWorkshopXml.ClassToXml(req, id));
    }

    public static XElement Root(IEnumerable<XElement> record) => new("elements", record.Select(e => new XElement(e)));
}

public sealed record HomebrewSpellsResponse(List<HomebrewSpellModel> Spells);

public sealed record HomebrewSubclassesResponse(List<HomebrewSubclassModel> Subclasses);

public sealed record HomebrewClassesResponse(List<HomebrewClassModel> Classes);

public sealed record SubclassTargetsResponse(List<SubclassTarget> Targets);

// ---- spells ----

public sealed class GetHomebrewSpellsEndpoint : EndpointWithoutRequest<HomebrewSpellsResponse>
{
    public override void Configure()
    {
        Get("/homebrew/spells");
        AllowAnonymous();
        Group<ElementsGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var document = HomebrewWorkshop.Load(Config, HomebrewWorkshop.SpellsFile);
        await Send.OkAsync(new HomebrewSpellsResponse(HomebrewWorkshop.Mains(document, "Spell")
            .Select(e => HomebrewWorkshopXml.SpellFromXml(e) with { Revision = HomebrewWorkshopReview.Revision(document, (string)e.Attribute("id")!) })
            .OrderBy(s => s.Level).ThenBy(s => s.Name).ToList()), ct);
    }
}

/// <summary>Adds a homebrew spell, or changes the one with the same id, and imports it.</summary>
/// <remarks>With an expected revision (a preview's), a spell changed or removed since is not saved: 409 with a fresh preview.</remarks>
public sealed class SaveHomebrewSpellEndpoint : Endpoint<HomebrewSpellModel, HomebrewSpellModel>
{
    private readonly IAuroraImporter _importer;
    private readonly IElementUsage _usage;

    public SaveHomebrewSpellEndpoint(IAuroraImporter importer, IElementUsage usage)
    {
        _importer = importer;
        _usage = usage;
    }

    public override void Configure()
    {
        Put("/homebrew/spells");
        AllowAnonymous();
        Group<ElementsGroup>();
        Description(b => b.Produces<HomebrewEditPreview>(StatusCodes.Status409Conflict, "application/json"));
    }

    public override async Task HandleAsync(HomebrewSpellModel req, CancellationToken ct)
    {
        if (HomebrewWorkshopReview.Spell(req).Count > 0)
        {
            AddError("A homebrew spell needs a name (100 characters at most), a level from 0 to 9, a school, and class lists made of letters.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }
        await HomebrewContent.Gate.WaitAsync(ct);
        try
        {
            var document = HomebrewWorkshop.Load(Config, HomebrewWorkshop.SpellsFile);
            var id = string.IsNullOrWhiteSpace(req.Id) ? HomebrewWorkshopXml.IdFor("SPELL", req.Name) : req.Id!;
            var xml = HomebrewWorkshopXml.SpellToXml(req, id);
            if (req.ExpectedRevision is { } expected && HomebrewWorkshopReview.Revision(document, id) is var current && current != expected)
            {
                var before = HomebrewWorkshop.Main(document, "Spell", id) is { } main ? HomebrewWorkshopXml.SpellFromXml(main) : null;
                await HttpContext.Response.SendAsync(await HomebrewWorkshopReview.BuildAsync("spells", document, id, before, HomebrewWorkshopXml.SpellFromXml(xml), [xml],
                    [HomebrewWorkshopReview.Stale(id, current)], _usage, ct), StatusCodes.Status409Conflict, cancellation: ct);
                return;
            }
            await HomebrewWorkshop.SaveAsync(Config, _importer, HomebrewWorkshop.SpellsFile, document, id, [xml], ct);
            await Send.OkAsync(HomebrewWorkshop.Saved(Config, HomebrewWorkshop.SpellsFile, id, (saved, e) => HomebrewWorkshopXml.SpellFromXml(e) with { Revision = HomebrewWorkshopReview.Revision(saved, id) }), ct);
        }
        finally
        {
            HomebrewContent.Gate.Release();
        }
    }
}

// ---- subclasses ----

public sealed class GetHomebrewSubclassesEndpoint : EndpointWithoutRequest<HomebrewSubclassesResponse>
{
    public override void Configure()
    {
        Get("/homebrew/subclasses");
        AllowAnonymous();
        Group<ElementsGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var document = HomebrewWorkshop.Load(Config, HomebrewWorkshop.SubclassesFile);
        await Send.OkAsync(new HomebrewSubclassesResponse(HomebrewWorkshop.Mains(document, "Archetype")
            .Select(e => HomebrewWorkshopXml.SubclassFromXml(document.Root!, e) with { Revision = HomebrewWorkshopReview.Revision(document, (string)e.Attribute("id")!) })
            .OrderBy(s => s.ClassName).ThenBy(s => s.Name).ToList()), ct);
    }
}

/// <summary>The classes a subclass can be made for: every class in the content with a subclass choice, and the homebrew classes.</summary>
public sealed class GetSubclassTargetsEndpoint : EndpointWithoutRequest<SubclassTargetsResponse>
{
    private readonly IPersistence _persistence;

    public GetSubclassTargetsEndpoint(IPersistence persistence) => _persistence = persistence;

    public override void Configure()
    {
        Get("/homebrew/subclass-targets");
        AllowAnonymous();
        Group<ElementsGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct) => await Send.OkAsync(new SubclassTargetsResponse(await Targets(_persistence, Config)), ct);

    public static async Task<List<SubclassTarget>> Targets(IPersistence persistence, IConfiguration config)
    {
        var elements = await persistence.GetRepository<IElementsRepository>().GetAuroraElementsAsync(["Class", "Class Feature"]);
        var targets = HomebrewWorkshopXml.SubclassTargets(elements.Select(e => (e.AuroraType, e.AuroraId, e.Name, e.Source, e.RawXml)));
        var classes = HomebrewWorkshop.Load(config, HomebrewWorkshop.ClassesFile);
        foreach (var main in HomebrewWorkshop.Mains(classes, "Class"))
        {
            var model = HomebrewWorkshopXml.ClassFromXml(classes.Root!, main);
            if (model.SubclassSupports is { } supports && !targets.Any(t => t.Supports == supports))
            {
                targets.Add(new SubclassTarget(model.Name, model.Source, supports));
            }
        }
        return targets;
    }
}

/// <summary>Adds a homebrew subclass for a class's subclass choice, or changes the one with the same id, and imports it.</summary>
/// <remarks>With an expected revision (a preview's), a subclass changed or removed since is not saved: 409 with a fresh preview.</remarks>
public sealed class SaveHomebrewSubclassEndpoint : Endpoint<HomebrewSubclassModel, HomebrewSubclassModel>
{
    private readonly IAuroraImporter _importer;
    private readonly IPersistence _persistence;
    private readonly IElementUsage _usage;

    public SaveHomebrewSubclassEndpoint(IAuroraImporter importer, IPersistence persistence, IElementUsage usage)
    {
        _importer = importer;
        _persistence = persistence;
        _usage = usage;
    }

    public override void Configure()
    {
        Put("/homebrew/subclasses");
        AllowAnonymous();
        Group<ElementsGroup>();
        Description(b => b.Produces<HomebrewEditPreview>(StatusCodes.Status409Conflict, "application/json"));
    }

    public override async Task HandleAsync(HomebrewSubclassModel req, CancellationToken ct)
    {
        var targets = await GetSubclassTargetsEndpoint.Targets(_persistence, Config);
        if (HomebrewWorkshopReview.Subclass(req, targets).Count > 0)
        {
            AddError("A homebrew subclass needs a name, a class it belongs to, and features with a level from 1 to 20 and a name.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }
        await HomebrewContent.Gate.WaitAsync(ct);
        try
        {
            var document = HomebrewWorkshop.Load(Config, HomebrewWorkshop.SubclassesFile);
            var id = string.IsNullOrWhiteSpace(req.Id) ? HomebrewWorkshopXml.IdFor("SUBCLASS", req.Name) : req.Id!;
            var record = HomebrewWorkshopXml.SubclassToXml(req, id);
            if (req.ExpectedRevision is { } expected && HomebrewWorkshopReview.Revision(document, id) is var current && current != expected)
            {
                var before = HomebrewWorkshop.Main(document, "Archetype", id) is { } main ? HomebrewWorkshopXml.SubclassFromXml(document.Root!, main) : null;
                await HttpContext.Response.SendAsync(await HomebrewWorkshopReview.BuildAsync("subclasses", document, id, before, HomebrewWorkshopXml.SubclassFromXml(HomebrewWorkshop.Root(record), record[0]), record,
                    [HomebrewWorkshopReview.Stale(id, current)], _usage, ct), StatusCodes.Status409Conflict, cancellation: ct);
                return;
            }
            await HomebrewWorkshop.SaveAsync(Config, _importer, HomebrewWorkshop.SubclassesFile, document, id, record, ct);
            await Send.OkAsync(HomebrewWorkshop.Saved(Config, HomebrewWorkshop.SubclassesFile, id,
                (saved, e) => HomebrewWorkshopXml.SubclassFromXml(saved.Root!, e) with { Revision = HomebrewWorkshopReview.Revision(saved, id) }), ct);
        }
        finally
        {
            HomebrewContent.Gate.Release();
        }
    }
}

// ---- classes ----

public sealed class GetHomebrewClassesEndpoint : EndpointWithoutRequest<HomebrewClassesResponse>
{
    public override void Configure()
    {
        Get("/homebrew/classes");
        AllowAnonymous();
        Group<ElementsGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var document = HomebrewWorkshop.Load(Config, HomebrewWorkshop.ClassesFile);
        await Send.OkAsync(new HomebrewClassesResponse(HomebrewWorkshop.Mains(document, "Class")
            .Select(e => HomebrewWorkshopXml.ClassFromXml(document.Root!, e) with { Revision = HomebrewWorkshopReview.Revision(document, (string)e.Attribute("id")!) })
            .OrderBy(c => c.Name).ToList()), ct);
    }
}

/// <summary>Adds a homebrew class, or changes the one with the same id (its subclasses' and spells' keys stay), and imports it.</summary>
/// <remarks>With an expected revision (a preview's), a class changed or removed since is not saved: 409 with a fresh preview.</remarks>
public sealed class SaveHomebrewClassEndpoint : Endpoint<HomebrewClassModel, HomebrewClassModel>
{
    private readonly IAuroraImporter _importer;
    private readonly IElementUsage _usage;

    public SaveHomebrewClassEndpoint(IAuroraImporter importer, IElementUsage usage)
    {
        _importer = importer;
        _usage = usage;
    }

    public override void Configure()
    {
        Put("/homebrew/classes");
        AllowAnonymous();
        Group<ElementsGroup>();
        Description(b => b.Produces<HomebrewEditPreview>(StatusCodes.Status409Conflict, "application/json"));
    }

    public override async Task HandleAsync(HomebrewClassModel req, CancellationToken ct)
    {
        if (HomebrewWorkshopReview.Class(req).Count > 0)
        {
            AddError("A homebrew class needs a name made of letters, a hit die (d6–d12), proficiencies from the lists, levels from 1 to 20, and a spellcasting ability when it casts.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }
        await HomebrewContent.Gate.WaitAsync(ct);
        try
        {
            var document = HomebrewWorkshop.Load(Config, HomebrewWorkshop.ClassesFile);
            var id = string.IsNullOrWhiteSpace(req.Id) ? HomebrewWorkshopXml.IdFor("CLASS", req.Name) : req.Id!;
            var (_, record) = HomebrewWorkshop.ClassRecord(document, req, id);
            if (req.ExpectedRevision is { } expected && HomebrewWorkshopReview.Revision(document, id) is var current && current != expected)
            {
                var before = HomebrewWorkshop.Main(document, "Class", id) is { } main ? HomebrewWorkshopXml.ClassFromXml(document.Root!, main) : null;
                await HttpContext.Response.SendAsync(await HomebrewWorkshopReview.BuildAsync("classes", document, id, before, HomebrewWorkshopXml.ClassFromXml(HomebrewWorkshop.Root(record), record[0]), record,
                    [HomebrewWorkshopReview.Stale(id, current)], _usage, ct), StatusCodes.Status409Conflict, cancellation: ct);
                return;
            }
            await HomebrewWorkshop.SaveAsync(Config, _importer, HomebrewWorkshop.ClassesFile, document, id, record, ct);
            await Send.OkAsync(HomebrewWorkshop.Saved(Config, HomebrewWorkshop.ClassesFile, id,
                (saved, e) => HomebrewWorkshopXml.ClassFromXml(saved.Root!, e) with { Revision = HomebrewWorkshopReview.Revision(saved, id) }), ct);
        }
        finally
        {
            HomebrewContent.Gate.Release();
        }
    }
}

// ---- previews ----

/// <summary>
/// What saving a homebrew spell would do, without saving: POST the form as a save would PUT it. Admin only, like the
/// save (every non-GET under /api/elements), since it names the characters that use it.
/// </summary>
public sealed class PreviewHomebrewSpellEndpoint : Endpoint<HomebrewSpellModel, HomebrewEditPreview>
{
    private readonly IElementUsage _usage;

    public PreviewHomebrewSpellEndpoint(IElementUsage usage) => _usage = usage;

    public override void Configure()
    {
        Post("/homebrew/spells/preview");
        AllowAnonymous();
        Group<ElementsGroup>();
    }

    public override async Task HandleAsync(HomebrewSpellModel req, CancellationToken ct)
    {
        var diagnostics = HomebrewWorkshopReview.Spell(req);
        var document = await HomebrewWorkshop.ReadAsync(Config, HomebrewWorkshop.SpellsFile, ct);
        var id = string.IsNullOrWhiteSpace(req.Id) ? HomebrewWorkshopXml.IdFor("SPELL", req.Name ?? string.Empty) : req.Id!;
        var main = HomebrewWorkshop.Main(document, "Spell", id);
        if (main is not null && string.IsNullOrWhiteSpace(req.Id))
        {
            diagnostics.Add(HomebrewWorkshopReview.ReplacesExisting(id, (string?)main.Attribute("name") ?? id));
        }
        HomebrewWorkshopReview.CheckOpened(diagnostics, document, id, req.Revision);
        var xml = diagnostics.Any(d => d.Severity == ImportDiagnosticSeverity.Error) ? null : HomebrewWorkshopXml.SpellToXml(req, id);
        await Send.OkAsync(await HomebrewWorkshopReview.BuildAsync("spells", document, id, main is null ? null : HomebrewWorkshopXml.SpellFromXml(main),
            xml is null ? null : HomebrewWorkshopXml.SpellFromXml(xml), xml is null ? null : [xml], diagnostics, _usage, ct), ct);
    }
}

/// <summary>What saving a homebrew subclass would do, without saving (see the spell's preview).</summary>
public sealed class PreviewHomebrewSubclassEndpoint : Endpoint<HomebrewSubclassModel, HomebrewEditPreview>
{
    private readonly IPersistence _persistence;
    private readonly IElementUsage _usage;

    public PreviewHomebrewSubclassEndpoint(IPersistence persistence, IElementUsage usage)
    {
        _persistence = persistence;
        _usage = usage;
    }

    public override void Configure()
    {
        Post("/homebrew/subclasses/preview");
        AllowAnonymous();
        Group<ElementsGroup>();
    }

    public override async Task HandleAsync(HomebrewSubclassModel req, CancellationToken ct)
    {
        var diagnostics = HomebrewWorkshopReview.Subclass(req, await GetSubclassTargetsEndpoint.Targets(_persistence, Config));
        var document = await HomebrewWorkshop.ReadAsync(Config, HomebrewWorkshop.SubclassesFile, ct);
        var id = string.IsNullOrWhiteSpace(req.Id) ? HomebrewWorkshopXml.IdFor("SUBCLASS", req.Name ?? string.Empty) : req.Id!;
        var main = HomebrewWorkshop.Main(document, "Archetype", id);
        if (main is not null && string.IsNullOrWhiteSpace(req.Id))
        {
            diagnostics.Add(HomebrewWorkshopReview.ReplacesExisting(id, (string?)main.Attribute("name") ?? id));
        }
        HomebrewWorkshopReview.CheckOpened(diagnostics, document, id, req.Revision);
        var record = diagnostics.Any(d => d.Severity == ImportDiagnosticSeverity.Error) ? null : HomebrewWorkshopXml.SubclassToXml(req, id);
        await Send.OkAsync(await HomebrewWorkshopReview.BuildAsync("subclasses", document, id, main is null ? null : HomebrewWorkshopXml.SubclassFromXml(document.Root!, main),
            record is null ? null : HomebrewWorkshopXml.SubclassFromXml(HomebrewWorkshop.Root(record), record[0]), record, diagnostics, _usage, ct), ct);
    }
}

/// <summary>What saving a homebrew class would do, without saving (see the spell's preview).</summary>
public sealed class PreviewHomebrewClassEndpoint : Endpoint<HomebrewClassModel, HomebrewEditPreview>
{
    private readonly IElementUsage _usage;

    public PreviewHomebrewClassEndpoint(IElementUsage usage) => _usage = usage;

    public override void Configure()
    {
        Post("/homebrew/classes/preview");
        AllowAnonymous();
        Group<ElementsGroup>();
    }

    public override async Task HandleAsync(HomebrewClassModel req, CancellationToken ct)
    {
        var diagnostics = HomebrewWorkshopReview.Class(req);
        var document = await HomebrewWorkshop.ReadAsync(Config, HomebrewWorkshop.ClassesFile, ct);
        var id = string.IsNullOrWhiteSpace(req.Id) ? HomebrewWorkshopXml.IdFor("CLASS", req.Name ?? string.Empty) : req.Id!;
        var main = HomebrewWorkshop.Main(document, "Class", id);
        if (main is not null && string.IsNullOrWhiteSpace(req.Id))
        {
            diagnostics.Add(HomebrewWorkshopReview.ReplacesExisting(id, (string?)main.Attribute("name") ?? id));
        }
        HomebrewWorkshopReview.CheckOpened(diagnostics, document, id, req.Revision);
        var record = diagnostics.Any(d => d.Severity == ImportDiagnosticSeverity.Error) ? null : HomebrewWorkshop.ClassRecord(document, req, id).Record;
        await Send.OkAsync(await HomebrewWorkshopReview.BuildAsync("classes", document, id, main is null ? null : HomebrewWorkshopXml.ClassFromXml(document.Root!, main),
            record is null ? null : HomebrewWorkshopXml.ClassFromXml(HomebrewWorkshop.Root(record), record[0]), record, diagnostics, _usage, ct), ct);
    }
}

// ---- removing ----

/// <summary>Removes a homebrew spell, subclass or class (with its features). Characters that used it lose it at their next update.</summary>
public sealed class DeleteHomebrewWorkshopEndpoint : EndpointWithoutRequest
{
    private readonly IAuroraImporter _importer;

    public DeleteHomebrewWorkshopEndpoint(IAuroraImporter importer) => _importer = importer;

    public override void Configure()
    {
        Delete("/homebrew/{kind}/{id}");
        AllowAnonymous();
        Group<ElementsGroup>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var file = Route<string>("kind") switch
        {
            "spells" => HomebrewWorkshop.SpellsFile,
            "subclasses" => HomebrewWorkshop.SubclassesFile,
            "classes" => HomebrewWorkshop.ClassesFile,
            _ => null,
        };
        var id = Route<string>("id") ?? string.Empty;
        if (file is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }
        await HomebrewContent.Gate.WaitAsync(ct);
        try
        {
            var document = HomebrewWorkshop.Load(Config, file);
            if (!HomebrewWorkshopXml.RecordElements(document.Root!, id).Any())
            {
                await Send.NotFoundAsync(ct);
                return;
            }
            await HomebrewWorkshop.SaveAsync(Config, _importer, file, document, id, null, ct);
            await Send.NoContentAsync(ct);
        }
        finally
        {
            HomebrewContent.Gate.Release();
        }
    }
}
