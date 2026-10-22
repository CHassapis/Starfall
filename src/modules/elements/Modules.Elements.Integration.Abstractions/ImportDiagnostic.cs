using System.Text.Json.Serialization;

namespace Starlights.Modules.Elements.Integration;

/// <summary>
/// Something an import noticed and could not use: a file it could not read, an index entry that is not in the
/// content, an append whose target is not loaded, a grant of an element that does not exist. The same shape for every
/// importer (Aurora content, what is generated from 5etools, homebrew files and the Homebrew page, an Aurora character
/// file), so the web app shows them all one way. Only what an import actually ran into is reported. Additive: the text
/// warnings and counts that came before stay as they were.
/// </summary>
public sealed record ImportDiagnostic(ImportDiagnosticSeverity Severity, ImportDiagnosticCode Code, ImportDiagnosticImporter Importer, string Message)
{
    /// <summary>The file it is about, as the import names it ("homebrew/my-feats.xml", "core/players-handbook.xml").</summary>
    public string? File { get; init; }

    /// <summary>The Aurora id of the element it is about.</summary>
    public string? ElementId { get; init; }

    public string? ElementName { get; init; }

    /// <summary>What was looked for and not found (the granted id, the index entry, the 5etools reference).</summary>
    public string? Detail { get; init; }

    /// <summary>Set on the last entry of a code when more were found than are listed: how many more.</summary>
    public int? Omitted { get; init; }

    /// <summary>The importer a file belongs to, by where it is filed.</summary>
    public static ImportDiagnosticImporter ImporterOf(string? file) =>
        file?.StartsWith("homebrew/", StringComparison.Ordinal) == true ? ImportDiagnosticImporter.Homebrew
        : file?.StartsWith("5etools/", StringComparison.Ordinal) == true ? ImportDiagnosticImporter.FiveETools
        : ImportDiagnosticImporter.Aurora;

    /// <summary>
    /// In a fixed order (the most serious first, then by code, file, element and detail), whatever order the files
    /// were read in, with at most <paramref name="perCode"/> of each code: a whole Aurora import can run into
    /// thousands of the same thing. The last one listed of a code says how many more there were.
    /// </summary>
    public static IReadOnlyList<ImportDiagnostic> Arrange(IEnumerable<ImportDiagnostic> diagnostics, int perCode = 50)
    {
        var arranged = new List<ImportDiagnostic>();
        var ordered = diagnostics
            .Distinct()
            .OrderBy(d => d.Severity)
            .ThenBy(d => d.Code)
            .ThenBy(d => d.File, StringComparer.Ordinal)
            .ThenBy(d => d.ElementId, StringComparer.Ordinal)
            .ThenBy(d => d.Detail, StringComparer.Ordinal)
            .ThenBy(d => d.Message, StringComparer.Ordinal);
        foreach (var group in ordered.GroupBy(d => (d.Severity, d.Code)))
        {
            var all = group.ToList();
            var listed = all.Take(perCode).ToList();
            if (all.Count > perCode)
            {
                listed[^1] = listed[^1] with { Omitted = all.Count - perCode };
            }
            arranged.AddRange(listed);
        }
        return arranged;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<ImportDiagnosticSeverity>))]
public enum ImportDiagnosticSeverity
{
    /// <summary>Nothing was saved: what was sent has to change first.</summary>
    [JsonStringEnumMemberName("error")] Error,

    /// <summary>Saved, but part of it was left out or will not work as written.</summary>
    [JsonStringEnumMemberName("warning")] Warning,

    [JsonStringEnumMemberName("info")] Info,
}

[JsonConverter(typeof(JsonStringEnumConverter<ImportDiagnosticImporter>))]
public enum ImportDiagnosticImporter
{
    /// <summary>The Aurora content repository (and its open pull requests).</summary>
    [JsonStringEnumMemberName("aurora")] Aurora,

    /// <summary>What is generated from the 5etools data.</summary>
    [JsonStringEnumMemberName("5etools")] FiveETools,

    /// <summary>Uploaded homebrew files and what the Homebrew page makes.</summary>
    [JsonStringEnumMemberName("homebrew")] Homebrew,

    /// <summary>An Aurora character file (.dnd5e) imported as a character.</summary>
    [JsonStringEnumMemberName("aurora-character")] AuroraCharacter,
}

/// <summary>What happened. New codes can be added; a client shows an unknown one by its message.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ImportDiagnosticCode>))]
public enum ImportDiagnosticCode
{
    /// <summary>A file is not valid XML (or JSON) and was skipped.</summary>
    [JsonStringEnumMemberName("unreadable-file")] UnreadableFile,

    /// <summary>An .index file lists a file that is not in the content repository.</summary>
    [JsonStringEnumMemberName("index-entry-missing")] IndexEntryMissing,

    /// <summary>A homebrew &lt;append&gt; names an element that is not loaded.</summary>
    [JsonStringEnumMemberName("append-target-missing")] AppendTargetMissing,

    /// <summary>An element grants an id that is not in the content: that grant is left out.</summary>
    [JsonStringEnumMemberName("grant-unresolved")] GrantUnresolved,

    /// <summary>A 5etools class or subclass lists a feature the data does not have: that feature is left out.</summary>
    [JsonStringEnumMemberName("class-feature-missing")] ClassFeatureMissing,

    /// <summary>A 5etools subclass is for a class that is in neither Aurora nor 5etools: the subclass is left out.</summary>
    [JsonStringEnumMemberName("subclass-class-missing")] SubclassClassMissing,

    /// <summary>An Aurora character file picks an element that is not in the imported content.</summary>
    [JsonStringEnumMemberName("element-missing")] ElementMissing,

    /// <summary>An Aurora character file answers a choice that did not come up for the character here.</summary>
    [JsonStringEnumMemberName("choice-unmatched")] ChoiceUnmatched,

    /// <summary>A value on the Homebrew page is not allowed (a level out of range, an empty name).</summary>
    [JsonStringEnumMemberName("invalid-value")] InvalidValue,

    /// <summary>A new homebrew record gets the id of an existing one (the same name) and would take its place.</summary>
    [JsonStringEnumMemberName("replaces-existing")] ReplacesExisting,

    /// <summary>The homebrew record changed (or was removed) after the preview this save was based on.</summary>
    [JsonStringEnumMemberName("stale-revision")] StaleRevision,
}
