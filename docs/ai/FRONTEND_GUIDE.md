# Frontend guide
All paths relative to `src/frontend/apps/builder-app/src` unless stated otherwise.

## Components and state
`main.tsx` defines routes. `App.tsx` / `pages/layouts/default-layout.tsx` supply header, navigation and footer. `components/ui` wraps Radix controls. Reuse Button, Dialog, Drawer, Input, Spinner and `ElementDetails` (sanitized HTML). QueryClient owns server state; local React state owns navigation/search. `lib/api-client.ts` attaches unlock headers. Do not bypass it.

## Styling
Tailwind 4 imports workspace `packages/ui/src/index.css` through app `index.css`. Semantic background, foreground, card, border, primary and muted tokens should replace fixed white/black surfaces. Theme provider uses the existing `starlights-ui-theme` storage key. Preserve legacy light/dark/system preferences. Theme UI must remain keyboard accessible and support reduced motion.

## Builder and magic
`pages/characters/builder/builder-page.tsx` renders Build, Abilities, Magic, Equipment, Story and Sources. `wizard.tsx` is guided creation; `lib/rules/wizard-steps.ts:stepChoices` and `spellChoiceGroups` determine choice grouping. `ChoiceRow` uses server eligibility and choice APIs. `magic-tab.tsx:MagicTab` displays per-class casting, prepared spells, slots and next-level summary. `lib/api/magic.ts` provides index/casting/magic/save hooks; `lib/rules/magic.ts` is authoritative client rules. Never infer new selection limits from spell list lengths. `save-bar.tsx` owns Save/Discard; selection feedback must not claim a builder snapshot was saved.

## Sheet
`lib/api/sheet.ts:useSheetData` and `lib/sheet-model.ts` supply existing display data. Printable `pages/characters/sheet/sheet-page.tsx` and `lib/aurora-sheet.ts` are retained. On-screen sheet must reuse combat, magic and inventory hooks; no separate character store.

## Battle
`pages/battle/battle-page.tsx` contains shell, vitals, status, action choices and RestDialog. `items.tsx`, `companions.tsx`, `prepare-spells.tsx` are companions. `lib/battle-model.ts` builds available actions; `lib/rules/battle.ts`, `item-uses.ts`, `item-spells.ts` are mechanics. Style changes must not change combat calculations. Campaign encounter UI is separate and protected.

## Validation
Run workspace package build before app build; API/client package exports use dist. Vitest tests under `lib/rules` cover spell progression, multiclass, combat, inventory and encounters. Browser tests should use synthetic intercepted API responses on an isolated dev server, never production mutations. Check 320/390/768/1024/1440 widths, all themes, focus, expansion, filtering, errors and persistence. PDF tests need templates/assets present.

## Modernization additions
- `styles/themes.css`: Porcelain (light), Slate and Midnight (dark) semantic palettes, reduced motion and shared surfaces. `lib/themes.ts:isDarkTheme` decides the `dark` class and color scheme; `index.html` applies the saved theme before the first paint (keep it in step with `normalizeTheme`). `components/mode-toggle.tsx`: live preview, cancel and save; provider migrates existing light/dark settings.
- `components/ornaments.tsx:StarfallShell` + `styles/ornament.css`: the one page frame (both app layouts) with the supplied page corners and edge jewels on its border line; tile corners (`OrnamentalCorner`) only on feature cards (home tiles, Lore banner, campaign cards). Assets per theme in `public/ornaments/<theme>/`; `tools/frontend/ornaments.mjs` checks attachment, themes, print and missing files; `homepage-branding.mjs` the hero composition.
- Ornament vocabulary (2026-10-21 polish; all in `styles/ornament.css`, theme pictures via `--ornament-*` variables, none takes clicks, print drops them): page titles (`h1.font-heading`, `h2.font-heading.text-2xl`: also Lore entries, Compendium detail and book chapters) get the section marker on a fading rule; `data-ornament` = panel corners top-left + bottom-right (dialogs automatically, home brand block, character list header), `data-ornament="corner"` = top-left only (support panels, campaign sessions, sheet header); `CelestialDivider` between major sections (home); `.ornament-section-head` for a section's heading row (builder sections: metal icon, fading rule); `.celestial-empty` for one-line empty states and `[data-slot="empty"]` boxes get corners; `.ornament-sprig` once (campaign header, no cover, from 1024px); `.lore-page` the book reader's inner rule. Battle, encounter, tables and forms get nothing beyond the frame. Utilities beat this components layer, so remove a conflicting `pb-*`/`border-b`/text colour on the element instead of adding `!`.
- Large monitors: `3xl` (2000px) / `4xl` (2560px) breakpoints and the type step live in `styles/themes.css`, the wider frame caps in `ornament.css`; widen a tool page with `3xl:max-w-*` on its own container, keep prose and form widths.
- `components/spells/spell-browser.tsx`: SpellBrowser/SpellCard, document-flow pagination and lazy descriptions. `pages/characters/builder/spell-choice.tsx`: Learn/Remove via existing hooks, server requirements, explicit printing selector. `lib/rules/spell-options.ts:groupSpellOptions`: reprint grouping without changing stored ids. Same-feature duplicate selection guard stays in SpellChoice.
- `/characters/:id/play`: `pages/characters/sheet/interactive-sheet.tsx:InteractiveSheetPage`. Reuses `Vitals`, `Status` and a narrowed `RestDialog` from battle-page, `MagicTab`, `EquipmentTab`, and original API hooks. Ability/attack summaries clearly identify base values; temporary effects/actions remain in battle.
- `/characters/:id/sheet` remains the original PDF route. Only a reciprocal link was added. No PDF generation code changed.
- `tools/frontend/smoke.mjs` and `fixtures.mjs`: synthetic browser regression harness; intercepts every API request. Start Vite with `VITE_API_BASE=''`, then run `PLAYWRIGHT_MODULE=/absolute/path/to/playwright/index.mjs node tools/frontend/smoke.mjs` from root. Override FRONTEND_URL and SCREENSHOT_DIR as needed. Screenshots default to /tmp/starlights-frontend-shots. Never point at a deployed application.

## Final validation tools
`tools/frontend/workflows.mjs`, `complete-characters.mjs`, `play-checks.mjs` use a disposable real SQL/API at localhost:55053 and Vite at localhost:55052. They are not mocks and must never target production. See CODEX_HANDOFF.md for isolation, order and evidence. `accessibility.mjs` uses synthetic intercepted responses to test keyboard navigation and failed-save rollback. The fixture matrix includes 360px as well as the original five widths.

The sheet owns expansion state above loading guards because preparing a spell may refresh dependent element queries. Theme selection uses DialogTrigger for reliable focus return. Save failures use existing API rollback; rest/slot notices announce pending saves without claiming success prematurely. The original imported content and mechanics remain authoritative.

Official assets live in builder-app/public/branding; source SHA and crop reproduction are in its README. Full logo is on the landing header, compass in navigation, central D20 in favicons. Local-only development defaults are in vite.config.ts.
