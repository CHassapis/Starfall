/**
 * The import diagnostics contract: what an import (Aurora content, 5etools, homebrew, an Aurora character file) ran
 * into, one shape for all of them. Generated from docs/api/openapi.json by src/lib/contracts/openapi-contract.ts; do
 * not edit by hand (the contract test fails when it differs). Regenerate after the server's contract changes:
 *   STARLIGHTS_UPDATE_CONTRACTS=1 npm test -w apps/builder-app -- contracts
 */

export interface ImportDiagnostic {
  severity: ImportDiagnosticSeverity;
  code: ImportDiagnosticCode;
  importer: ImportDiagnosticImporter;
  message: string;
  file?: null | string;
  elementId?: null | string;
  elementName?: null | string;
  detail?: null | string;
  omitted?: null | number;
  count?: null | number;
}

export interface UploadHomebrewResponse {
  file: string;
  elementsImported: number;
  elementsReplaced: number;
  warnings: string[];
  diagnostics: ImportDiagnostic[];
  entries?: ImportEntryCounts;
}

export interface DeleteHomebrewResponse {
  removed: number;
  warnings: string[];
  diagnostics: ImportDiagnostic[];
  entries?: ImportEntryCounts;
}

export type ImportDiagnosticSeverity = "error" | "warning" | "info";

export const ImportDiagnosticSeverityValues = ["error", "warning", "info"] as const satisfies readonly ImportDiagnosticSeverity[];

export type ImportDiagnosticCode = "unreadable-file" | "index-entry-missing" | "append-target-missing" | "grant-unresolved" | "class-feature-missing" | "subclass-class-missing" | "element-missing" | "choice-unmatched" | "invalid-value" | "replaces-existing" | "stale-revision" | "duplicate-id" | "element-incomplete" | "requirement-unsupported" | "text-only" | "entries-skipped";

export const ImportDiagnosticCodeValues = ["unreadable-file", "index-entry-missing", "append-target-missing", "grant-unresolved", "class-feature-missing", "subclass-class-missing", "element-missing", "choice-unmatched", "invalid-value", "replaces-existing", "stale-revision", "duplicate-id", "element-incomplete", "requirement-unsupported", "text-only", "entries-skipped"] as const satisfies readonly ImportDiagnosticCode[];

export type ImportDiagnosticImporter = "aurora" | "5etools" | "homebrew" | "aurora-character";

export const ImportDiagnosticImporterValues = ["aurora", "5etools", "homebrew", "aurora-character"] as const satisfies readonly ImportDiagnosticImporter[];

export interface ImportEntryCounts {
  complete: number;
  partial: number;
  textOnly: number;
  skipped: number;
}
