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

## Stage 3 (done): branch `feature/structured-mechanics` from 238b571, worktree `~/starfall-structured-mechanics`
- 193ee7a homebrew spell mechanics (roll, save + half, damage terms, healing, modifier, cantrip tiers, slot dice) as
  `starlights-spell-*` setters, only when a roll is set; preview and save refuse the same; second 400 message.
- 02f78a6 battle view reads mechanics instead of the text (never both); `spellEffect` adds the damage bonus and
  records `spell:<slug>:<level>:dice|bonus`; "Why?" on spell cards; Homebrew form "What it rolls";
  `tools/frontend/spell-mechanics.mjs`.
- ae5837e `CharacterCombat.Resources` (nullable: a save without it keeps them), caps, SQL Server tests (row lock).
- 223f5b5 server's web-key list knows `spell:` and `resource:` keys.
- 0b8e9e9 own resources merged into `buildBattleModel(data, resources)` for sheet and battle; `usesKeyOf`
  (`custom:<id>`); derived maximum traced (`resource:<id>:max`); pools via `findPool`; effects `custom:<id>` via
  `effectFor`; `dawn`/`never` recharges, "1/Day" now dawn (one test expectation changed on purpose); "Dawn" in both
  views; `tools/frontend/custom-resources.mjs`.

Checks: see the Stage 3 PR (full backend with SQL Server, web, tsc, eslint baseline, browser checks, Docker build).

Limits: mechanics only for Homebrew page spells (Aurora/5etools spells still read from text); a spell with mechanics
is not read from its text at all; resource effects cover armor class, speed, attack rolls and weapon damage (not
saving throws, which the views do not read); rests switch resource effects off like other effects; item charges still
come back by hand (not at dawn); a resource's maximum is worked out by the web app, the server only checks bounds.

## Stage 4 (done): branch `feature/pdf-worker-favourite-rolls` from 0814b1a, worktree `~/starfall-sheet-rolls`
Stage 3 PR: #31 (full backend with SQL Server 936: 912 passed, 24 skipped, 0 failed).

PDF baseline before any change (`tools/frontend/sheet-performance.mjs`, `fixture({ large: true })`: 72 spells, 40
features with long made-up texts, 12 pages; desktop Chromium, 3 runs): making the PDF ran as one task of 2247 / 2308 /
2274 ms on the page's thread; the page did not answer for 3312 / 3253 / 3960 ms (a 20 ms timer's longest wait);
"Download PDF" ready after 6.8–7.4 s. Drawing the pages with pdf.js afterwards: longest task 71–101 ms.

Commits:
1. 1a0122a PDF in a module worker (`lib/aurora-sheet.worker.ts`, `lib/aurora-sheet-client.ts`; Forge's
   sheet-render-worker pattern): HTML paragraphs read on the page first, pictures through OffscreenCanvas, the page
   makes it when the worker cannot. After (final code, 3 runs): longest task while the PDF is made 120 / 120 / 114 ms,
   longest timer wait 147 / 136 / 134 ms, "Download PDF" ready after 5.5 s; drawing the pages: no long task.
   `sheet-pdf-worker.mjs`: the 13 pages (with a portrait) are the same pictures from the worker and from the page; a
   template that cannot load gives the same message.
2. 4d41510 `CharacterCombat.FavoriteRolls` (id, name, formula; at most 40; kept when a save leaves them out).
   `CharacterFavoriteRoll.IsDice` accepts what `parseDice` does; both sides test `lib/lore/dice-cases.json`. Null
   entries (and a resource or roll without id, name or maximum) are refused with 400 instead of failing.
3. 444e6b8 `pages/battle/favorite-rolls.tsx` in the battle view (panel below the target) and the sheet ("Resources &
   rests"); rolled with `lib/lore/dice.ts`, shown with the compendium's toast (`components/lore/dice-toast.ts`) and a
   "Last roll" line. The battle view's `parseRoll` is for damage (never below 0, a total only), so it is not used.
   Flare's pinned rolls credited (idea only, MPL-2.0).
4. Docs: API guide "Favorite rolls", this checkpoint, handoff Coordination.
5. dc84c2e The server ignores the same spaces as JavaScript's `\s` (three more shared cases); empty entries are
   refused with 400 at the endpoint (SQL Server test).

Checks: backend with SQL Server 942: 918 passed, 24 skipped, 0 failed; web 365 passed, 5 skipped; TypeScript clean;
ESLint 38 errors, 1 warning (as before); frontend Docker image builds; browser checks `favorite-rolls` 50,
`sheet-pdf-worker` 21, `smoke` 90, `calculation-traces` 36, `accessibility`, `custom-resources` 50, `spell-mechanics`
38, `homebrew-review` 48, `ornaments` 110, `units` 17; `sheet-performance` with `MAX_BLOCK_MS=500` passed.

Limits: the PDF still takes about 5.5 s on the large character (now beside the page); one task of about 120 ms stays
on the page while it is made; a browser without module workers makes it on the page as before. Favorite rolls are plain
dice (no advantage, no damage type), results are not saved, and the order is the order added.
