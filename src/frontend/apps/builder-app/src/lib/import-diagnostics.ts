import { ImportDiagnosticCodeValues, ImportDiagnosticSeverityValues, type ImportDiagnostic, type ImportEntryCounts } from "@/lib/contracts/import-diagnostics";

export type { ImportDiagnostic, ImportEntryCounts } from "@/lib/contracts/import-diagnostics";

const order = <T extends string>(values: readonly T[], value: T) => {
  const i = values.indexOf(value);
  return i < 0 ? values.length : i;
};

// by UTF-16 code units, nothing before anything, as the server's ordinal comparison
const text = (a: string | null | undefined, b: string | null | undefined) => (a === b ? 0 : a == null ? -1 : b == null ? 1 : a < b ? -1 : a > b ? 1 : 0);

const same = (a: ImportDiagnostic, b: ImportDiagnostic) =>
  a.severity === b.severity && a.code === b.code && a.importer === b.importer && a.message === b.message && a.file === b.file && a.elementId === b.elementId && a.detail === b.detail;

/**
 * As the server arranges them: the most serious first, then by code (in the contract's order), file, element and
 * detail, at most `perCode` of each code, the last one listed of a code saying how many more there were. For what the
 * web app finds itself (an Aurora character file).
 */
export function arrangeDiagnostics(diagnostics: ImportDiagnostic[], perCode = 50): ImportDiagnostic[] {
  const unique = diagnostics.filter((d, i) => diagnostics.findIndex((o) => same(o, d)) === i);
  const sorted = [...unique].sort(
    (a, b) =>
      order(ImportDiagnosticSeverityValues, a.severity) - order(ImportDiagnosticSeverityValues, b.severity) ||
      order(ImportDiagnosticCodeValues, a.code) - order(ImportDiagnosticCodeValues, b.code) ||
      text(a.file, b.file) ||
      text(a.elementId, b.elementId) ||
      text(a.detail, b.detail) ||
      text(a.message, b.message),
  );
  const out: ImportDiagnostic[] = [];
  const counts = new Map<string, ImportDiagnostic[]>();
  for (const d of sorted) {
    const key = `${d.severity}|${d.code}`;
    const list = counts.get(key) ?? [];
    list.push(d);
    counts.set(key, list);
  }
  for (const list of counts.values()) {
    const listed = list.slice(0, perCode);
    if (list.length > perCode) listed[listed.length - 1] = { ...listed[listed.length - 1], omitted: list.length - perCode };
    out.push(...listed);
  }
  return out;
}

/** How many of each severity, counting those a list leaves out ("and 70 more"). */
export function diagnosticCounts(diagnostics: ImportDiagnostic[]): Record<ImportDiagnostic["severity"], number> {
  const counts = { error: 0, warning: 0, info: 0 };
  for (const d of diagnostics) counts[d.severity] = (counts[d.severity] ?? 0) + 1 + (d.omitted ?? 0);
  return counts;
}

/** One line for the heading: "2 problems, 3 warnings". */
export function diagnosticSummary(diagnostics: ImportDiagnostic[]): string {
  const c = diagnosticCounts(diagnostics);
  const part = (n: number, one: string, many: string) => (n ? `${n} ${n === 1 ? one : many}` : null);
  return [part(c.error, "problem", "problems"), part(c.warning, "warning", "warnings"), part(c.info, "note", "notes")].filter(Boolean).join(", ");
}

/**
 * What an import did with its entries, the parts that are not 0: "4 in full · 2 in part · 1 text only · 3 skipped"
 * (text only: a feature with no rules, shown but not applied); null when it made and skipped nothing.
 */
export function entrySummary(e: ImportEntryCounts | null | undefined): string | null {
  if (!e) return null;
  const parts = [
    e.complete ? `${e.complete} in full` : null,
    e.partial ? `${e.partial} in part` : null,
    e.textOnly ? `${e.textOnly} text only` : null,
    e.skipped ? `${e.skipped} skipped` : null,
  ].filter(Boolean);
  return parts.length ? parts.join(" · ") : null;
}

/** Two imports' counts added up (a batch of homebrew files). */
export function addEntries(a: ImportEntryCounts | null | undefined, b: ImportEntryCounts | null | undefined): ImportEntryCounts {
  const zero: ImportEntryCounts = { complete: 0, partial: 0, textOnly: 0, skipped: 0 };
  const [x, y] = [a ?? zero, b ?? zero];
  return { complete: x.complete + y.complete, partial: x.partial + y.partial, textOnly: x.textOnly + y.textOnly, skipped: x.skipped + y.skipped };
}

const IMPORTER_NAMES: Record<ImportDiagnostic["importer"], string> = { aurora: "Aurora content", "5etools": "5etools", homebrew: "Homebrew", "aurora-character": "Aurora character file" };

/** Where it is: the file, and the element by name with its id ("homebrew/feats.xml · Brew Feat (ID_BREW_FEAT)"). */
export function diagnosticWhere(d: ImportDiagnostic): string {
  const element = d.elementName && d.elementId ? `${d.elementName} (${d.elementId})` : (d.elementName ?? d.elementId);
  return [IMPORTER_NAMES[d.importer] ?? d.importer, d.file, element].filter(Boolean).join(" · ");
}

interface AuroraPick {
  name: string;
  type: string;
  registered: string;
}

/** An Aurora character file's pick whose element is not in the imported content. */
export function missingPickDiagnostic(p: AuroraPick): ImportDiagnostic {
  return {
    severity: "warning",
    code: "element-missing",
    importer: "aurora-character",
    message: `${p.name} (${p.type}) is not in the imported content, so it was left out. Its homebrew or book may not be imported.`,
    elementId: p.registered,
    elementName: p.name,
    detail: p.type,
  };
}

/** An Aurora character file's pick for a choice that did not come up for the character here. */
export function unmatchedPickDiagnostic(p: AuroraPick): ImportDiagnostic {
  return {
    severity: "warning",
    code: "choice-unmatched",
    importer: "aurora-character",
    message: `${p.name} was chosen in the file, but that choice did not come up for this character here, so it was not picked.`,
    elementId: p.registered,
    elementName: p.name,
    detail: p.type,
  };
}
