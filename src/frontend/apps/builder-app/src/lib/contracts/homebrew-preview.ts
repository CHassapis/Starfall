/**
 * What saving a Homebrew page edit would do: what changes, who uses it, and what is wrong with it. Generated from
 * docs/api/openapi.json by src/lib/contracts/openapi-contract.ts; do not edit by hand (the contract test fails when it
 * differs). Regenerate after the server's contract changes:
 *   STARLIGHTS_UPDATE_CONTRACTS=1 npm test -w apps/builder-app -- contracts
 */

export interface HomebrewEditPreview {
  kind: string;
  id: string;
  isNew: boolean;
  revision: null | string;
  changes: HomebrewFieldChange[];
  elements: HomebrewElementChange[];
  characters: HomebrewAffectedCharacter[];
  diagnostics: ImportDiagnostic[];
}

export interface HomebrewFieldChange {
  field: string;
  before: null | string;
  after: null | string;
}

export interface HomebrewElementChange {
  elementId: string;
  name: string;
  change: HomebrewElementChangeKind;
}

export interface HomebrewAffectedCharacter {
  characterId: string;
  name: string;
  playerName: null | string;
  uses: string[];
  loses: string[];
}

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
}

export type HomebrewElementChangeKind = "added" | "changed" | "removed";

export const HomebrewElementChangeKindValues = ["added", "changed", "removed"] as const satisfies readonly HomebrewElementChangeKind[];

export type ImportDiagnosticSeverity = "error" | "warning" | "info";

export const ImportDiagnosticSeverityValues = ["error", "warning", "info"] as const satisfies readonly ImportDiagnosticSeverity[];

export type ImportDiagnosticCode = "unreadable-file" | "index-entry-missing" | "append-target-missing" | "grant-unresolved" | "class-feature-missing" | "subclass-class-missing" | "element-missing" | "choice-unmatched" | "invalid-value" | "replaces-existing" | "stale-revision";

export const ImportDiagnosticCodeValues = ["unreadable-file", "index-entry-missing", "append-target-missing", "grant-unresolved", "class-feature-missing", "subclass-class-missing", "element-missing", "choice-unmatched", "invalid-value", "replaces-existing", "stale-revision"] as const satisfies readonly ImportDiagnosticCode[];

export type ImportDiagnosticImporter = "aurora" | "5etools" | "homebrew" | "aurora-character";

export const ImportDiagnosticImporterValues = ["aurora", "5etools", "homebrew", "aurora-character"] as const satisfies readonly ImportDiagnosticImporter[];
