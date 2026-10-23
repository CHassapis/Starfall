# Claude handoff

Updated 2026-10-23 (warnings and errors session). Keep under ~80 lines; replace stale lines.

## Task (open): warnings and errors (`fix/warnings-and-errors`, `~/starfall-warnings`, from cf3eb56)
Build 89 warnings → 0 (vulnerable Pkcs/MessagePack pinned, MSTest duplicates, EF1002, obsolete test helpers,
test cancellation tokens), frontend chunk-size warning (PDF sheet route lazy, chunk groups), API log WRN/ERR
(EF comparer + query splitting, HTTPS redirect, shutdown, removed registrations, "pending registrations" → debug),
failed-import cleanup SQL (`character_snapshot.character_id`), Aurora list picks (personality traits) on import.
Details in the PR. Chunk groups take whole packages, never `maxSize`: size splits made import cycles between chunks
and the app rendered blank ("bt is not a function") while vitest, eslint and the image build passed.

## Previous task (merged #29–#32; live cf3eb56 since 2026-10-23 06:40): calculation traces, import reports +
homebrew review, spell mechanics + own resources, PDF worker + favorite rolls. Checkpoint:
[CALCULATION_TRACES_CHECKPOINT.md](CALCULATION_TRACES_CHECKPOINT.md); deploy evidence + kit:
`~/.cache/starlights-test/deploy-cf3eb56/` (LEDGER.md, deploy.sh). Polish #22–#27 live since 2026-10-21.

## Earlier work
Visual fidelity ([VISUAL_FIDELITY_REPORT.md](VISUAL_FIDELITY_REPORT.md), 2026-10-21); Mk2 stabilization, v0.11.0 (2026-10-20, [MK2_VERIFICATION_REPORT.md](MK2_VERIFICATION_REPORT.md)); homebrew workshop
+ encounter difficulty, API compatibility / custom attacks, v0.10.0: details in git history and
[FINAL_HANDOFF.md](FINAL_HANDOFF.md).

## Not done / known limits
- Windows (Docker Desktop, WSL2, `setup.ps1`) never run on real Windows.
- PWA install checked by emulation (iPhone 13, Pixel 7) and Chrome's installability check, not on real devices.
- Builder: an edit made in the first seconds after levelling, while the save snapshot waits for processing, is not
  undone by Discard (kept, not lost).
- Resistance order can differ from Aurora's; battle page violet chip contrast 3.3–3.9:1; Characters cards nest
  their export/delete buttons in the card's control (axe `nested-interactive`).
- 9 npm advisories left after #16, all in build tooling, each needing a shadcn CLI or `@tailwindcss/typography`
  major.
- `--skip-content` install + manual `./admin.sh import` needs `/api/elements/initialize` first (the Content page /
  content update does it). Custom-monster CR calculator, homebrew Mystic Arcanum/third-casters: not built.
- Character files exported before #18 can't move between servers; export again (#18 is live since 2026-10-20).

## Coordination (shared files touched)
- Warnings: `main.tsx` (the printable sheet route `/characters/:id/sheet` is `lazy`, like `:id/play`),
  `vite.config.ts` of builder-app (whole-package chunk groups, ES worker) and showcase,
  `lib/aurora-character-import.ts`.
- Calculation traces: `pages/characters/sheet/interactive-sheet.tsx` (Codex's): "Why this value?" buttons
  (`components/why-value.tsx`) beside figures, a "Why these values" row under `Vitals` (Vitals itself unchanged),
  and the spellcasting's attack and DC above the Magic tab. `tools/frontend/fixtures.mjs`: answers
  `…/calculation/trace` (synthetic). `lib/api/sheet.ts`: pure figures moved to `lib/sheet-figures.ts`; spell attack/DC
  now the server's. `lib/rules/items.ts`, `lib/sheet-model.ts`: optional trace recorder (figures unchanged).
- #15: `pages/battle/battle-page.tsx` (`UsePips`: `role="img"`), `pages/compendium/Index.tsx` (two `aria-label`s).
- Docs PR: `tools/frontend/custom-attacks.mjs` (builder route `/characters/:id`), `workflows.mjs` (waits for the
  builder session before editing), `accessibility.mjs` (`FRONTEND_URL` override). Codex's other tools unchanged.
- Stage 3 (spell mechanics, own resources): `pages/battle/battle-page.tsx` (Codex's): one `spellEffect` call with the
  damage bonus (`withBonus` gone) and a "Why?" beside a spell's dice; "Dawn" beside the rests and a dawn branch in
  `RestDialog` (`RestKind`); the model takes `state.resources`; uses go through `usesKeyOf`; resource controls from the
  new `pages/battle/custom-resources.tsx`. `pages/characters/sheet/interactive-sheet.tsx`: the same model input, a
  "Dawn" button, resource counts by `usesKeyOf` with "Why?" for a resource's maximum. `pages/homebrew/workshop-tabs.tsx`:
  "What it rolls" section. `lib/api/sheet.ts`: `SheetSpell.mechanics`, `SheetData.classLevels`. Tools:
  `spell-mechanics.mjs`, `custom-resources.mjs` (new); fixtures unchanged.
- Stage 4 (PDF worker, favorite rolls): `pages/characters/sheet/sheet-page.tsx` makes the PDF with `makeSheetPdf`
  (`lib/aurora-sheet-client.ts`: a module worker, `lib/aurora-sheet.worker.ts`, with the page as fallback);
  `lib/aurora-sheet.ts` takes pre-read HTML paragraphs and draws pictures through `newCanvas`/`canvasJpeg` in
  `lib/portrait-render.ts`; `spellLevelLine` moved to `lib/spell-level-line.ts` (`lib/api/sheet.ts` re-exports it).
  `pages/battle/battle-page.tsx` (Codex's): a "Favorite rolls" panel below the target panel.
  `pages/characters/sheet/interactive-sheet.tsx`: the same list under "Resources & rests". Both use the new
  `pages/battle/favorite-rolls.tsx`; the compendium's dice toast is now `components/lore/dice-toast.ts`. Tools:
  `fixtures.mjs` has `fixture({ large: true })` (the default is unchanged); `sheet-performance.mjs`,
  `sheet-pdf-worker.mjs`, `favorite-rolls.mjs` (new).
- Visual fidelity, polish and Mk2 (merged 2026-10-20/21): themes, ornaments, layout, landing and `tools/frontend`
  contracts; per-file details in those PRs and VISUAL_FIDELITY_REPORT.md.
## How to verify things here (reusable)
- Disposable install of a worktree: `~/.cache/starlights-test/mk2/e2e-setup.sh REPO DB API_PORT WEB_PORT`
  (SQL container `starfall-mk2-sql` on network `starfall-mk2-test`, removed 2026-10-20; recreate both first:
  `docker network create starfall-mk2-test`, then `docker run -d --name starfall-mk2-sql --network starfall-mk2-test
  -e ACCEPT_EULA=Y -e MSSQL_PID=developer -e MSSQL_SA_PASSWORD="$(cat ~/.cache/starlights-test/mk2/sqlpw)"
  mcr.microsoft.com/mssql/server:2022-latest`); real characters via `~/.cache/starlights-test/pw/mk2-e2e.mjs` (its
  "errors 0" counts page errors only: read `import-report.txt` for per-file failures).
- Community kit: `deploy/installer-tests/docker_smoke.py --run` (isolated project, random 127.0.0.1 port).
  For `update.sh`, clone from GitHub and bind 127.0.0.1 with a local test commit (default port 8093 = live).
- Lore data for a disposable web: run `lore-ingest/ingest.ts` with `--out` in a scratch folder, never
  `~/homelab/config/starlights-lore`.

## Next
Maintenance mode: bug fixes, one mission per branch, merged through pull requests once CI passes.

## Blockers
None in the code. Merges and deploys need the owner's go-ahead in the session; `deploy-cf3eb56/deploy.sh` is the
latest guarded deploy (finish2.sh method + restore-tested backup).
