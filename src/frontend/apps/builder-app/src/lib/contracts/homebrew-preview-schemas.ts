/** The schemas of a Homebrew page edit's preview (POST /api/elements/homebrew/{kind}/preview, and a stale save's 409), for the generated types. */
export const HOMEBREW_PREVIEW_SCHEMAS = ["HomebrewEditPreview"];

export const HOMEBREW_PREVIEW_HEADER = `/**
 * What saving a Homebrew page edit would do: what changes, who uses it, and what is wrong with it. Generated from
 * docs/api/openapi.json by src/lib/contracts/openapi-contract.ts; do not edit by hand (the contract test fails when it
 * differs). Regenerate after the server's contract changes:
 *   STARLIGHTS_UPDATE_CONTRACTS=1 npm test -w apps/builder-app -- contracts
 */`;
