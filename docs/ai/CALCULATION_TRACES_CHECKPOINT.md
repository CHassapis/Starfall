# Calculation traces: checkpoint

Branch `feature/calculation-traces` from `aurora-server` 6dbade3, worktree `~/starfall-calc-traces`, PR
https://github.com/CHassapis/Starfall/pull/29 (stage 1). Stages 2–4 go on branches of their own built on it, each
with its own PR to `aurora-server`. Updated 2026-10-22 (stage 2, with the brief's full list).

## Stage 1 (done, in this order)
- db3df0e, 9e1c4d7 reference characters: synthetic content, ten hand-worked specs, server suite (SqlServer category).
- b9f731a fix: `:half` variants no longer remove an ability's `:score:set` (regression test).
- e43721e fix: equal same-kind bonuses: the registration made first counts, whatever the load order (test).
- e3fab4b the statistics calculator records how it works (`StatisticsTraceRecorder`, `CalculationTraceBuilder`).
- 170741e `GET /api/characters/{id}/calculation/trace`, contract v1, endpoint/access/no-mutation tests, OpenAPI.
- 84cb314 web contract types generated from `docs/api/openapi.json` (drift test), real worked example.
- a2f1f23 web tracer (`lib/rules/trace.ts`) through armor class, attacks, hit points, speeds; `lib/sheet-figures.ts`.
- 5ebbd19 fix: the sheet's spell attack/DC are the server's (item bonuses were missing on sheet, battle, texts).
- 77002e9, 77ac625 web reference suite: sheet, PDF fields, battle view, spell slots, traces vs. hand figures.
- 18f5230 API guide section. Then: "Why this value?" on the interactive sheet + `tools/frontend/calculation-traces.mjs`.

## Checks (2026-10-22)
Characters unit tests 341; trace endpoint + access 24; server reference suite 12/12 (disposable SQL Server); web
288 passed / 5 skipped (baseline 262 / 5); tsc clean; eslint 38 errors / 1 warning (= baseline, none in new code);
browser: calculation-traces 36, smoke 90, ornaments 110, units 17, accessibility pass (qa.mjs stops at a logo removed
in 8b626cf, as on aurora-server). Full backend with SQL Server 893: 869 passed, 24 skipped. Docker frontend build ok.

## Known limits
- The import's DOM reading of an Aurora file is not run in tests (no DOM in vitest); its mapping is.
- Battle view: a spell outside a class spellcasting uses proficiency + modifier, without `spellcasting:attack|dc`.
- Requirements and "while wearing" conditions are applied before calculation; traces show their result.

## Stage 2 (done): branch `feature/import-diagnostics` from 175c4a6, worktree `~/starfall-import-diagnostics`
- 3b38047 `ImportDiagnostic` contract (Elements.Integration.Abstractions); Aurora importer reports unreadable files,
  missing index entries, homebrew appends without a target, unresolved grants (scoped to the import, sorted, 50 a
  code). On the import result, homebrew upload/removal, content job status; named response types; web types + test.
- 00bfd69 5etools: missing class/subclass feature references, subclasses of a class nowhere (synthetic data test).
- 7cfb7df one web list (`components/import-diagnostics.tsx`): content job, homebrew files, Aurora character import.
- 86ef7dd POST `/api/elements/homebrew/{kind}/preview`, record `revision`, PUT `expectedRevision` → 409 + preview;
  `IElementUsage` (implemented by Characters: registrations + prepared spells); `AuroraIds` shared hashing.
- a0a877c "Review and save" on the Homebrew page forms; `tools/frontend/homebrew-review.mjs`. 85fc55b API guide.
- d8ee758 a save is based on the revision the form was opened on (advisor review); class/subclass agreement tests.
- ea25cb4 test fix: the usage test waited for nothing before its snapshot (failed 2 of 3; 3 of 3 after).
- 3afee4a preview `invalidated` picks: spell off a list it is prepared from, subclass moved, class subclass key.
- ceb3a68 the brief's other half: `duplicate-id`, `element-incomplete`, `requirement-unsupported`, `text-only`,
  5etools `entries-skipped` (+ `count`), `ImportEntryCounts` (complete/partial/textOnly/skipped) on every import
  answer; `RequirementTerms` shared by importer and evaluator. 5d93cfa web: entries line, notes apart.

Checks: full backend with SQL Server 927: 903 passed, 24 skipped, 0 failed (stage 1: 893/869/24); web 305 passed / 5 skipped; tsc clean; eslint 38 errors / 1 warning (= baseline);
browser: homebrew-review 48, smoke 90, calculation-traces 36, ornaments 110, units 17, accessibility pass; Docker
frontend build ok. Elements unit 239 (231 passed, 8 skipped),
Characters unit 351.

Limits: a 5etools file that is not valid JSON still stops the import (skipping could drop generated features);
`skippedConditionalRules` is always 0, so no diagnostic; the preview loads every character for prepared spells; the
workshop delete has no preview; the in-memory test provider cannot save a homebrew file twice (tests on SQL Server);
a spell's invalidated pick matches the spellcasting by the list's name; a class's new subclass key is checked against
the Homebrew page's subclasses only; the Aurora character import's DOM reading is not run in tests.

## Later stages
3: structured homebrew spell mechanics in traces; custom resources shared by sheet and battle (no parallel engine).
4: PDF main-thread investigation; favourite rolls with the existing dice evaluator. Each on a branch built on the
previous one, with its own PR to `aurora-server`.
