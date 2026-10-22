# Claude handoff

Updated 2026-10-22 (calculation traces / import diagnostics session 52b845c7). Keep under ~80 lines; replace stale lines.

## Task (open): calculation traces (stage 1), import diagnostics + homebrew preview (stage 2), stages 3–4 to do
Stage 1: `feature/calculation-traces` (`~/starfall-calc-traces`, PR #29). Stage 2: `feature/import-diagnostics`
(`~/starfall-import-diagnostics`, built on stage 1). Status, commits, checks and next steps:
[CALCULATION_TRACES_CHECKPOINT.md](CALCULATION_TRACES_CHECKPOINT.md). Contracts: docs/api/README.md "Calculation
traces", "Imports and homebrew"; reference characters: `src/frontend/apps/builder-app/reference-characters/README.md`.

## Previous task (merged #22–#27; web live b5492d6 since 2026-10-21 18:17): polish & release readiness
D20 icosahedron, shared ornaments, Imperial/Metric units, PWA kit, README rewrite; `fix/ornament-cache` versions the
emblem URL. Details in the PRs and git history.

## Earlier task (merged; web live 1f47f91 since 2026-10-21 15:08): visual fidelity
[VISUAL_FIDELITY_REPORT.md](VISUAL_FIDELITY_REPORT.md): one page frame, mockup homepage, dark Slate.

## Earlier work
Mk2 stabilization, v0.11.0 (2026-10-20, [MK2_VERIFICATION_REPORT.md](MK2_VERIFICATION_REPORT.md)); homebrew workshop
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
  major. NuGet: transitive System.Security.Cryptography.Pkcs 9.0.4 note still open.
- `--skip-content` install + manual `./admin.sh import` needs `/api/elements/initialize` first (the Content page /
  content update does it). Custom-monster CR calculator, homebrew Mystic Arcanum/third-casters: not built.
- Character files exported before #18 can't move between servers; export again (#18 is live since 2026-10-20).

## Coordination (shared files touched)
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
- Visual fidelity: `styles/themes.css` (all three palettes; Slate dark), `lib/themes.ts` (`isDarkTheme`),
  `theme-provider.tsx`, `ui/sonner.tsx`, `index.html` (pre-paint theme), `default-layout.tsx` (header text/padding),
  `App.tsx`, `ornaments.tsx`, `ornament.css`, landing, `lore-home.tsx` banner, `campaigns-page.tsx` cards,
  `tools/frontend/ornaments.mjs` + `homepage-branding.mjs` (new contracts).
- Polish: `default-layout.tsx` (nav emblem, phone menu Install), `mode-toggle.tsx` (units, install), `main.tsx`
  (providers), `ornament.css`, `themes.css`, `index.html`, many pages (`3xl:` widths, `useUnits`), `deploy/nginx.conf`,
  README.md; the `Choose theme` label became `Choose theme and units` in five `tools/frontend` checks.
- Earlier (Mk2): `pages/landing/Index.tsx` hero, `styles/ornament.css`, `components/ornaments.tsx`, `App.tsx`,
  `lib/api/sheet.ts`, `lib/rules/proficiencies.ts`, `lib/rules/items.ts`, `lib/aurora-character-import.ts`.

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
None in the code. The session's auto-mode classifier refuses merges and production deploys; the owner runs them
(prepared scripts with guards, as `go.sh` / `finish2.sh` were).
