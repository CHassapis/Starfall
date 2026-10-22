/// <reference types="node" />
import { readFileSync, writeFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { checkShape, contractTypes, type OpenApiDocument } from "./openapi-contract";
import { CALCULATION_TRACE_HEADER, CALCULATION_TRACE_SCHEMAS } from "./calculation-trace-schemas";
import { HOMEBREW_PREVIEW_HEADER, HOMEBREW_PREVIEW_SCHEMAS } from "./homebrew-preview-schemas";
import { IMPORT_DIAGNOSTIC_HEADER, IMPORT_DIAGNOSTIC_SCHEMAS } from "./import-diagnostic-schemas";

// the API description the server's tests write (OpenApiDocumentTests), from the repository root
const description = new URL("../../../../../../../docs/api/openapi.json", import.meta.url);
const document = JSON.parse(readFileSync(description, "utf8")) as OpenApiDocument;
const example = new URL("../../../../../../../docs/api/examples/calculation-trace.json", import.meta.url);

describe("the calculation trace contract", () => {
  it("is the types the API description gives (regenerate with STARLIGHTS_UPDATE_CONTRACTS=1)", () => {
    const file = new URL("./calculation-trace.ts", import.meta.url);
    const generated = contractTypes(document, CALCULATION_TRACE_SCHEMAS, CALCULATION_TRACE_HEADER);
    if (process.env.STARLIGHTS_UPDATE_CONTRACTS === "1") writeFileSync(file, generated);
    expect(readFileSync(file, "utf8")).toBe(generated);
  });

  it("names the operations, reasons, kinds and outcomes the server does", () => {
    const values = (name: string) => document.components.schemas[name].enum;
    expect(values("TraceOperation")).toEqual(["start", "add", "replace", "maximum", "minimum", "multiply", "divide", "round-down", "round-up", "round-toward-zero"]);
    expect(values("TraceExclusionReason")).toEqual(["stacking", "lower-alternative", "set-not-higher", "condition-not-met", "unresolved-dependency"]);
    expect(values("TraceSourceKind")).toEqual(["rule", "user-supplied", "character", "fixed", "calculated"]);
    expect(values("TraceStatus")).toEqual(["traced", "missing", "unsupported"]);
  });

  it("the worked example in the API docs has the contract's shape", () => {
    const response = JSON.parse(readFileSync(example, "utf8"));
    expect(checkShape(document, "CalculationTraceResponse", response)).toEqual([]);
  });

  it("the shape check finds what does not fit", () => {
    const trace = { key: "armor-class", label: "Armor class", final: 15, steps: [{ operation: "plus", value: 1, result: 1, label: "x" }], extra: true };
    expect(checkShape(document, "CalculationTrace", trace)).toEqual([
      'CalculationTrace.steps[0].operation: "plus" is not one of start, add, replace, maximum, minimum, multiply, divide, round-down, round-up, round-toward-zero',
      "CalculationTrace.extra: not in the contract",
    ]);
    expect(checkShape(document, "CalculationTraceResponse", { characterId: "x", value: "skill:stealth" })).toEqual(["CalculationTraceResponse.status: missing"]);
  });
});

describe("the import diagnostics contract", () => {
  it("is the types the API description gives (regenerate with STARLIGHTS_UPDATE_CONTRACTS=1)", () => {
    const file = new URL("./import-diagnostics.ts", import.meta.url);
    const generated = contractTypes(document, IMPORT_DIAGNOSTIC_SCHEMAS, IMPORT_DIAGNOSTIC_HEADER);
    if (process.env.STARLIGHTS_UPDATE_CONTRACTS === "1") writeFileSync(file, generated);
    expect(readFileSync(file, "utf8")).toBe(generated);
  });

  it("names the severities, importers and codes the server does", () => {
    const values = (name: string) => document.components.schemas[name].enum;
    expect(values("ImportDiagnosticSeverity")).toEqual(["error", "warning", "info"]);
    expect(values("ImportDiagnosticImporter")).toEqual(["aurora", "5etools", "homebrew", "aurora-character"]);
    expect(values("ImportDiagnosticCode")).toEqual([
      "unreadable-file", "index-entry-missing", "append-target-missing", "grant-unresolved", "class-feature-missing", "subclass-class-missing",
      "element-missing", "choice-unmatched", "invalid-value", "replaces-existing", "stale-revision",
    ]);
  });

  it("the job status and the import result carry them, and the old text warnings stay", () => {
    const property = (schema: string, name: string) => document.components.schemas[schema].properties?.[name];
    expect(property("ContentJobStatus", "diagnostics")?.items?.$ref).toBe("#/components/schemas/ImportDiagnostic");
    expect(property("AuroraImportResult", "diagnostics")?.items?.$ref).toBe("#/components/schemas/ImportDiagnostic");
    expect(property("ContentJobStatus", "log")?.items?.type).toBe("string");
    expect(property("UploadHomebrewResponse", "warnings")?.items?.type).toBe("string");
    expect(checkShape(document, "ImportDiagnostic", { severity: "warning", code: "grant-unresolved", importer: "aurora", message: "x", omitted: null })).toEqual([]);
    expect(checkShape(document, "ImportDiagnostic", { severity: "fatal", code: "grant-unresolved", importer: "aurora" })).toEqual([
      "ImportDiagnostic.message: missing",
      'ImportDiagnostic.severity: "fatal" is not one of error, warning, info',
    ]);
  });
});

describe("the homebrew edit preview contract", () => {
  it("is the types the API description gives (regenerate with STARLIGHTS_UPDATE_CONTRACTS=1)", () => {
    const file = new URL("./homebrew-preview.ts", import.meta.url);
    const generated = contractTypes(document, HOMEBREW_PREVIEW_SCHEMAS, HOMEBREW_PREVIEW_HEADER);
    if (process.env.STARLIGHTS_UPDATE_CONTRACTS === "1") writeFileSync(file, generated);
    expect(readFileSync(file, "utf8")).toBe(generated);
  });

  it("a save may send the revision it read, and a stale one answers with a preview", () => {
    const schemas = document.components.schemas;
    for (const model of ["HomebrewSpellModel", "HomebrewSubclassModel", "HomebrewClassModel"]) {
      expect(Object.keys(schemas[model].properties ?? {})).toEqual(expect.arrayContaining(["revision", "expectedRevision"]));
    }
    expect(schemas.HomebrewElementChangeKind.enum).toEqual(["added", "changed", "removed"]);
    const paths = (document as unknown as { paths: Record<string, Record<string, { responses: Record<string, unknown> }>> }).paths;
    for (const kind of ["spells", "subclasses", "classes"]) {
      expect(Object.keys(paths[`/api/elements/homebrew/${kind}/preview`].post.responses)).toEqual(["200"]);
      expect(Object.keys(paths[`/api/elements/homebrew/${kind}`].put.responses)).toEqual(expect.arrayContaining(["200", "409"]));
    }
  });
});
