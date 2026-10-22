/**
 * The schemas of the import diagnostics contract (what an import ran into: the homebrew upload and removal, the
 * content update's job status, the Aurora import), for the generated types.
 */
export const IMPORT_DIAGNOSTIC_SCHEMAS = ["ImportDiagnostic", "UploadHomebrewResponse", "DeleteHomebrewResponse"];

export const IMPORT_DIAGNOSTIC_HEADER = `/**
 * The import diagnostics contract: what an import (Aurora content, 5etools, homebrew, an Aurora character file) ran
 * into, one shape for all of them. Generated from docs/api/openapi.json by src/lib/contracts/openapi-contract.ts; do
 * not edit by hand (the contract test fails when it differs). Regenerate after the server's contract changes:
 *   STARLIGHTS_UPDATE_CONTRACTS=1 npm test -w apps/builder-app -- contracts
 */`;
