# Calculation traces: checkpoint

Branch `feature/calculation-traces` from `aurora-server` 6dbade3, worktree `~/starfall-calc-traces`, PR
https://github.com/CHassapis/Starfall/pull/29 (stage 1). Stages 2–4 go on branches of their own built on it, each
with its own PR to `aurora-server`. Updated 2026-10-22.

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

## Later stages (after the Stage 1 PR)
2: import diagnostics (Aurora, 5etools, homebrew) + homebrew edit impact preview. 3: structured homebrew spell
mechanics in traces; custom resources shared by sheet and battle. 4: PDF main-thread investigation; favourite rolls.
