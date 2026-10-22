# Calculation traces: checkpoint

Branch `feature/calculation-traces` from `aurora-server` 6dbade3, worktree `~/starfall-calc-traces`. Not pushed yet;
publish to `mine` and open a PR to `aurora-server` (rulesets block direct pushes). Updated 2026-10-22.

## Done (commits)
- db3df0e reference characters: synthetic content `reference-characters/content`, ten hand-worked specs.
- 9e1c4d7 server reference suite (`Tests/Calculation/ReferenceCharacterTests.cs`, SqlServer category).
- b9f731a fix: `:half` variants no longer remove an ability's `:score:set` (regression test).
- e43721e fix: equal same-kind bonuses: the registration made first counts, whatever the load order (test).
- e3fab4b statistics calculator records how it works (`StatisticsTraceRecorder`, `CalculationTraceBuilder`).
- 170741e `GET /api/characters/{id}/calculation/trace`, contract v1, endpoint/access/no-mutation tests, OpenAPI.

## Checks so far
Characters unit tests 341 passed; trace endpoint + access 24 passed; reference suite 12/12 on a disposable SQL
Server. Baseline before the branch: backend 824 passed / 35 skipped, web 262 passed / 5 skipped.

## Stage 1 remaining, in order
1. Web: contract types generated from `docs/api/openapi.json` (drift test); `lib/rules/trace.ts`; tracer passed
   through armor class, attacks, hit points, speeds; pure sheet figures used by `useSheetData`; spell attack/DC
   from the server's casters (item bonuses were missing on the sheet, battle and placeholders) with a test.
2. Web reference suite `src/lib/reference-characters.test.ts`: spec figures, sheet/PDF/battle agreement, traces.
3. "Why this value?" on the interactive sheet (popover; drawer on phones); baseline vs. effects stated.
4. Docs (API README trace section + worked example), handoff Coordination note, full gates, publish checks, PR.

## Later stages (after the Stage 1 PR)
2: import diagnostics (Aurora, 5etools, homebrew) + homebrew edit impact preview. 3: structured homebrew spell
mechanics in traces; custom resources shared by sheet and battle. 4: PDF main-thread investigation; favourite rolls.
