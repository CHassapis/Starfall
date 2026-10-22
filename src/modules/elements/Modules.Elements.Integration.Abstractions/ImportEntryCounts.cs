namespace Starlights.Modules.Elements.Integration;

/// <summary>
/// What an import did with its entries, each counted once: imported in full; imported in part (a grant, a requirement
/// or a feature it refers to was left out); imported with only a description (a feature Starfall shows but applies
/// nothing from); skipped (not imported at all: an incomplete element, the copy of a repeated id that does not count,
/// 5etools entries left out on purpose). The categories follow Borealis's import report (converted, partial,
/// skipped), with text only apart.
/// </summary>
public sealed record ImportEntryCounts(int Complete, int Partial, int TextOnly, int Skipped)
{
    public static ImportEntryCounts None { get; } = new(0, 0, 0, 0);

    // what leaves part of an imported element out
    private static readonly HashSet<ImportDiagnosticCode> LeftOut =
        [ImportDiagnosticCode.GrantUnresolved, ImportDiagnosticCode.RequirementUnsupported, ImportDiagnosticCode.ClassFeatureMissing];

    /// <summary>
    /// The counts for the elements an import made (by Aurora id, and whether only their description says anything) and
    /// what it ran into (all of it, before <see cref="ImportDiagnostic.Arrange"/> shortens the list). An element a
    /// left-out warning names is partial, whatever else it is.
    /// </summary>
    public static ImportEntryCounts Of(IEnumerable<(string Id, bool TextOnly)> imported, IEnumerable<ImportDiagnostic> diagnostics)
    {
        var all = diagnostics.Distinct().ToList();
        var partial = all.Where(d => d.Severity == ImportDiagnosticSeverity.Warning && LeftOut.Contains(d.Code) && d.ElementId is not null)
            .Select(d => d.ElementId!).ToHashSet(StringComparer.Ordinal);
        int complete = 0, part = 0, text = 0;
        foreach (var (id, textOnly) in imported.DistinctBy(e => e.Id, StringComparer.Ordinal))
        {
            if (partial.Contains(id)) part++;
            else if (textOnly) text++;
            else complete++;
        }
        var skipped = all.Sum(d => d.Code switch
        {
            ImportDiagnosticCode.DuplicateId or ImportDiagnosticCode.ElementIncomplete or ImportDiagnosticCode.SubclassClassMissing => 1,
            ImportDiagnosticCode.EntriesSkipped => d.Count ?? 0,
            _ => 0,
        });
        return new ImportEntryCounts(complete, part, text, skipped);
    }

    public static ImportEntryCounts operator +(ImportEntryCounts a, ImportEntryCounts b) =>
        new(a.Complete + b.Complete, a.Partial + b.Partial, a.TextOnly + b.TextOnly, a.Skipped + b.Skipped);
}
