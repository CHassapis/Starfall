# Claude handoff

Updated 2026-10-21 (polish session 33d95646). Keep under ~80 lines; replace stale lines.

## Task (merged #22–#27; web live b5492d6 since 2026-10-21 18:17): polish & release readiness
Owner's mission + three packs (D20 refinement, universal UI assets, PWA home-screen kit). Worktree
`~/starfall-polish`, branches stacked from `aurora-server` `1f47f91`; merge **in order** #22 → #23 → #24 → #26 → docs PR:
1. #22 `feature/polish-brand-responsive`: D20 redrawn as a true icosahedron (`tools/frontend/d20-emblem.mjs`), the
   kit's app icons in `public/icons` (`tools/frontend/app-icons.py`), `3xl`/`4xl` breakpoints + type step for
   1440p/4K/ultrawide.
2. #23 `feature/polish-ornament`: shared ornament rules (title marker, corners, empty-state accent, sprig, book rule).
3. #24 `feature/polish-units`: Imperial/Metric display preference (`lib/units.ts`, `useUnits()`), no data change.
4. #26 `feature/polish-pwa`: manifest, install dialog/menu (`lib/install.ts`), no service worker; `deploy/nginx.conf`
   opens manifest + `/icons/` on the gated door (needs owner review before deploy).
5. `feature/polish-docs`: README rewritten as the entry point (13 sections, HTTPS via Tailscale Serve), user guide
   moved to `docs/using-starfall.md`, new synthetic screenshots.
Checks per stage in each PR; `tools/frontend/{units,pwa}.mjs` new. Evidence: `~/.cache/starlights-test/polish/`.
#25 (another session, `fix/backup-outside-compose`) edits `deploy/maintenance` + `docs/maintenance.md`: untouched here.
After go-live an iPhone kept the old D20 (unversioned `/ornaments/` file, no Cache-Control, Safari reused it):
`fix/ornament-cache` versions the emblem URL (`?v=2`) and serves `/ornaments/` + `/icons/` with `no-cache`.

## Previous task (merged; web live 1f47f91 since 2026-10-21 15:08): visual fidelity
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
- #15: `pages/battle/battle-page.tsx` (`UsePips`: `role="img"`), `pages/compendium/Index.tsx` (two `aria-label`s).
- Docs PR: `tools/frontend/custom-attacks.mjs` (builder route `/characters/:id`), `workflows.mjs` (waits for the
  builder session before editing), `accessibility.mjs` (`FRONTEND_URL` override). Codex's other tools unchanged.
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
