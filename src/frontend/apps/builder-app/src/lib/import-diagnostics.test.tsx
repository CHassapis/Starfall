/// <reference types="node" />
// What an import ran into, as the web app arranges, counts and shows it; and an Aurora character file's picks in
// the diagnostics' shape (the same contract the server's imports use).
import { readFileSync } from "node:fs";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ImportDiagnostics } from "@/components/import-diagnostics";
import { checkShape, type OpenApiDocument } from "@/lib/contracts/openapi-contract";
import { arrangeDiagnostics, diagnosticCounts, diagnosticSummary, diagnosticWhere, missingPickDiagnostic, unmatchedPickDiagnostic, type ImportDiagnostic } from "./import-diagnostics";

const document = JSON.parse(readFileSync(new URL("../../../../../../docs/api/openapi.json", import.meta.url), "utf8")) as OpenApiDocument;

const grant = (i: number): ImportDiagnostic => ({ severity: "warning", code: "grant-unresolved", importer: "aurora", message: `grant ${i}`, file: "core/a.xml", elementId: `ID_${String(i).padStart(3, "0")}` });

describe("import diagnostics in the web app", () => {
  it("are arranged as the server arranges them: most serious first, by code, at most fifty of a code", () => {
    const unreadable: ImportDiagnostic = { severity: "warning", code: "unreadable-file", importer: "homebrew", message: "broken", file: "homebrew/z.xml" };
    const blocking: ImportDiagnostic = { severity: "error", code: "invalid-value", importer: "homebrew", message: "no name" };
    const all = [...Array.from({ length: 120 }, (_, i) => grant(119 - i)), unreadable, blocking, { ...blocking }];

    const arranged = arrangeDiagnostics(all);

    expect(arranged).toHaveLength(52);
    expect(arranged.slice(0, 2)).toEqual([blocking, unreadable]);
    expect(arranged.slice(2).map((d) => d.elementId)).toEqual(Array.from({ length: 50 }, (_, i) => `ID_${String(i).padStart(3, "0")}`));
    expect(arranged.at(-1)!.omitted).toBe(70);
    expect(diagnosticCounts(arranged)).toEqual({ error: 1, warning: 121, info: 0 });
    expect(diagnosticSummary(arranged)).toBe("1 problem, 121 warnings");
  });

  it("say where: the importer, the file, and the element by name and id", () => {
    expect(diagnosticWhere({ ...grant(7), elementName: "Brew Feat" })).toBe("Aurora content · core/a.xml · Brew Feat (ID_007)");
    expect(diagnosticWhere({ severity: "warning", code: "subclass-class-missing", importer: "5etools", message: "x", file: "5etools/content.xml", elementName: "Lost Path" })).toBe(
      "5etools · 5etools/content.xml · Lost Path",
    );
  });

  it("an Aurora character file's missing and unmatched picks have the contract's shape", () => {
    const missing = missingPickDiagnostic({ name: "Brew Feat", type: "Feat", registered: "ID_BREW_FEAT" });
    const unmatched = unmatchedPickDiagnostic({ name: "Arcana", type: "Proficiency", registered: "ID_PROFICIENCY_SKILL_ARCANA" });
    expect([missing.code, missing.importer, missing.elementId, unmatched.code, unmatched.elementName]).toEqual(["element-missing", "aurora-character", "ID_BREW_FEAT", "choice-unmatched", "Arcana"]);
    for (const d of [missing, unmatched]) expect(checkShape(document, "ImportDiagnostic", d)).toEqual([]);
    expect(arrangeDiagnostics([unmatched, missing]).map((d) => d.code)).toEqual(["element-missing", "choice-unmatched"]);
  });

  it("are shown one way: counted, each with its message and where, and how many more are not listed", () => {
    const html = renderToStaticMarkup(<ImportDiagnostics title="Saved, but the homebrew needs attention" diagnostics={arrangeDiagnostics(Array.from({ length: 52 }, (_, i) => grant(i)))} />);
    expect(html).toContain('role="status"');
    expect(html).toContain("Saved, but the homebrew needs attention: 52 warnings");
    expect(html).toContain("grant 0");
    expect(html).toContain("Aurora content · core/a.xml · ID_000");
    expect(html).toContain("… and 2 more like this, not listed.");
    expect(html).not.toContain("<details open");
    expect(renderToStaticMarkup(<ImportDiagnostics diagnostics={[{ severity: "error", code: "invalid-value", importer: "homebrew", message: "A name is needed." }]} />)).toContain('role="alert"');
    expect(renderToStaticMarkup(<ImportDiagnostics diagnostics={[]} />)).toBe("");
  });
});
