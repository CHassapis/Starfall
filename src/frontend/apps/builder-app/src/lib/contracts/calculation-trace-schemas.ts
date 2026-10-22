/** The schemas of the calculation trace contract (GET /api/characters/{id}/calculation/trace), for the generated types. */
export const CALCULATION_TRACE_SCHEMAS = ["CalculationTraceResponse", "CalculationTraceError"];

export const CALCULATION_TRACE_HEADER = `/**
 * The calculation trace contract, version 1: how a figure on the sheet was worked out. Generated from
 * docs/api/openapi.json by src/lib/contracts/openapi-contract.ts; do not edit by hand (the contract test fails when
 * it differs). Regenerate after the server's contract changes:
 *   STARLIGHTS_UPDATE_CONTRACTS=1 npm test -w apps/builder-app -- contracts
 */`;
