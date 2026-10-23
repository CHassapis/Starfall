/**
 * The calculation trace contract, version 1: how a figure on the sheet was worked out. Generated from
 * docs/api/openapi.json by src/lib/contracts/openapi-contract.ts; do not edit by hand (the contract test fails when
 * it differs). Regenerate after the server's contract changes:
 *   STARLIGHTS_UPDATE_CONTRACTS=1 npm test -w apps/builder-app -- contracts
 */

export interface CalculationTraceResponse {
  contractVersion?: number;
  characterId: string;
  value: string;
  status: TraceStatus;
  trace?: null | CalculationTrace;
  content?: null | CalculationTraceContent;
  reason?: null | string;
}

export interface CalculationTraceError {
  contractVersion?: number;
  code: string;
  message: string;
}

export type TraceStatus = "traced" | "missing" | "unsupported";

export const TraceStatusValues = ["traced", "missing", "unsupported"] as const satisfies readonly TraceStatus[];

export interface CalculationTrace {
  key: string;
  label: string;
  final: number;
  steps?: TraceStep[];
  excluded?: TraceExclusion[];
  notes?: string[];
}

export interface CalculationTraceContent {
  sources?: string[];
  editions?: string[];
  fingerprint: string;
}

export interface TraceStep {
  operation: TraceOperation;
  value: number;
  result: number;
  label: string;
  source?: null | TraceSource;
  input?: null | CalculationTrace;
}

export interface TraceExclusion {
  reason: TraceExclusionReason;
  value: number;
  label: string;
  source?: null | TraceSource;
  detail?: null | string;
}

export type TraceOperation = "start" | "add" | "replace" | "maximum" | "minimum" | "multiply" | "divide" | "round-down" | "round-up" | "round-toward-zero";

export const TraceOperationValues = ["start", "add", "replace", "maximum", "minimum", "multiply", "divide", "round-down", "round-up", "round-toward-zero"] as const satisfies readonly TraceOperation[];

export interface TraceSource {
  kind: TraceSourceKind;
  name?: null | string;
  elementType?: null | string;
  elementId?: null | string;
  registrationId?: null | string;
  ruleId?: null | string;
  bonus?: null | string;
  refers?: null | string;
}

export type TraceExclusionReason = "stacking" | "lower-alternative" | "set-not-higher" | "condition-not-met" | "unresolved-dependency";

export const TraceExclusionReasonValues = ["stacking", "lower-alternative", "set-not-higher", "condition-not-met", "unresolved-dependency"] as const satisfies readonly TraceExclusionReason[];

export type TraceSourceKind = "rule" | "user-supplied" | "character" | "fixed" | "calculated";

export const TraceSourceKindValues = ["rule", "user-supplied", "character", "fixed", "calculated"] as const satisfies readonly TraceSourceKind[];
