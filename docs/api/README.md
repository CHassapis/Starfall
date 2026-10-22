# Starfall API — integration guide

Starfall is a web app on top of an HTTP API. Everything the app shows comes from that API, so another program (a
character viewer like [Flare](https://github.com/Egorobi/Flare), a Discord bot, a VTT bridge) can read the same data
from a Starfall server your group runs.

- **The description:** [`openapi.json`](openapi.json), OpenAPI 3.1, generated from the server's real endpoints (see
  [Keeping it current](#keeping-it-current)). Open it in any OpenAPI tool, or read it in the browser with
  [`index.html`](index.html) (Scalar; serve the folder, e.g. `python3 -m http.server -d docs/api`, or GitHub Pages).
- **Examples:** [`examples/starlights_client.py`](examples/starlights_client.py) (Python, standard library only) and
  [`examples/starlights-client.ts`](examples/starlights-client.ts) (TypeScript, `fetch`). Both log in as a player
  when needed and print each character's attack list, custom attacks included.

Nothing here needs a public server. A Starfall server is meant for a private network (your LAN, Tailscale); the
description is in this repository, and a production server does not serve it.
GitHub displays `index.html` as source: serve `docs/api` locally to use Scalar. Its pinned JavaScript bundle loads
from jsDelivr, so that viewer needs internet access; the JSON description and guide can be read offline.

## Reaching a server

The API is under `/api` on the same address as the web app: `http://<server>:8093/api/...` for a default Docker
install. JSON in and out, property names in camelCase.

- **From a program** (Python, Node, a desktop app): call it directly.
- **From a web page on another site:** outside development the server allows no other origins (CORS), so a browser
  page elsewhere cannot call it. Use a small server-side proxy, or run the page from the same address.
- An installation can put the whole site behind a password at the door (a reverse proxy with HTTP Basic auth, as the
  homelab install does for its public door). That is the proxy's, not the API's: send its `Authorization` header too.

## Players and passwords

There are no accounts. A **player** is the name characters are filed under (`playerName`); a player may be
**locked** with a password, which closes their characters to everyone else.

| | |
|---|---|
| `GET /api/characters/players` | `{ players: [{ name, characters, locked }] }` (a locked player's count is not given) |
| `POST /api/characters/players/unlock` | `{ name, password }` → `{ name, token }`; `401` for a wrong password (answered after a short delay), `429` after 5 tries a minute |
| `X-Player-Token` request header | the token(s): several players' tokens comma-separated. Valid 180 days, and only while the password stays the same |

What a token changes:

- `GET /api/characters` (the list) leaves out a locked player's characters unless the request carries their token.
- Everything under `/api/characters/{characterId}/…` of a locked player answers `401 { "locked": "<player>" }` without
  their token, reads and writes alike. An unlocked player's characters are open to everyone who can reach the server.

Not for integrations: the **admin** key (`X-Admin-Key`) and admin token, which change the books' content
(`/api/elements` writes, `/api/admin/...`), and the campaign DM tokens. Don't ask your group for them; a viewer
needs only a player's password, and only for a locked player.

## Characters

All take `{characterId}` (a GUID from the list). Read-only unless marked.

| Endpoint | What it gives |
|---|---|
| `GET /api/characters?player=<name>` | `{ characters: [{ characterId, name, level, build, portraitUrl, playerName }] }`; `player` is optional |
| `GET /api/characters/{id}` | `{ character }`: the same summary as in the list |
| `GET …/ability-scores` | `{ abilityScores: [{ abilityScoreId, name, abbreviation, baseScore, additionalScore, calculatedScore, calculatedModifier }] }` |
| `GET …/saving-throws`, `GET …/skills` | each with its ability, bonus parts and `calculatedBonus` |
| `GET …/statistics` | `{ statistics: [{ groupName, totalValue, values, isFinalized }] }`: every calculated statistic by Aurora's name (`proficiency`, speeds, …) with where each part comes from |
| `GET …/classes` | classes and levels |
| `GET …/features` | features and traits with their texts |
| `GET …/defenses` | resistances, immunities, vulnerabilities |
| `GET …/inventory` / `PUT` | items, coins, treasure, **custom attacks** (below) |
| `GET …/spellcasting` | each spellcasting: ability, attack bonus, save DC, slots, the spells it knows and prepares |
| `GET …/magic` / `PUT` | the player's choices: prepared spells, spent slots |
| `GET …/combat` / `PUT` | play state: damage taken, temporary HP, hit dice spent, conditions, exhaustion, death saves, uses of features and items |
| `GET …/story` | the story fields (appearance, backstory, …) |
| `GET …/export` | the whole character as one Starfall character file (see [character portability](../character-portability.md)) |
| `GET …/calculation/trace?value=<key>` | how one figure was worked out, step by step (see [Calculation traces](#calculation-traces)) |

Content the sheet needs alongside: `GET /api/elements/items` (every item's figures: weapon dice and properties, armor
class, by element id; default values are left out of the JSON) and `GET /api/elements/spell-index` (every spell).
Both send an `ETag`: send it back as `If-None-Match` and get `304` while nothing changed.

## Attacks

The sheet's attack list (on screen, in the PDF and in the Battle Action Simulator) is made of two kinds of rows,
both in `GET /api/characters/{id}/inventory`:

1. **Weapons on the list:** inventory `items` with an `attack` number (their place). The figures are worked out from
   the item (`/api/elements/items`: dice, properties, magic bonus) and the character (ability modifiers,
   proficiency, fighting styles), so they follow level-ups and new equipment. The API gives the parts; the web app
   does the arithmetic (`src/frontend/apps/builder-app/src/lib/rules/items.ts`, `attacks()`). No endpoint returns the
   finished weapon rows yet.
2. **Custom attacks:** `customAttacks`, written by hand and kept exactly as written, such as an artificer's armor
   attack or a natural weapon. No item stands behind them:

```json
"customAttacks": [
  { "id": "6f1d2c3b-…", "name": "Thunder Gauntlets", "range": "5 ft", "toHit": "+6 vs AC",
    "damage": "1d8+3 thunder", "ability": "Intelligence", "description": "…", "place": 2 }
]
```

Rows are shown in `attack`/`place` order (the two share one numbering). When nothing has a place, the sheet shows
the equipped weapons, then the custom attacks.

Where custom attacks come from: importing an Aurora character file (`.dnd5e`) keeps every displayed attack in
`<attacks>` whose identifier is not an item of the file (Aurora's **Manage → Attacks**), and the attack of an item
the server's content doesn't have. They keep Aurora's figures: Starfall does not recalculate them. Importing the
same file again updates unedited attacks by identifier and adds nothing twice. The Equipment tab’s Custom Attacks editor
sets `edited: true` on manual edits and reordered rows, which then take precedence over reimports. Deleted IDs
are remembered in inventory `removedAttackIds`, so reimports do not resurrect them. They stay through builder saves, level-ups,
trades, character export and import.

## Calculation traces

`GET /api/characters/{id}/calculation/trace?value=<key>&version=1` answers how one of the character's figures was
worked out, from the same calculation the other endpoints run (with a recorder), so the trace ends at the figure
they give. Read only: it never saves the character. Same access as the character's other endpoints (a locked
player's character needs `X-Player-Token`).

| Key | Value |
|---|---|
| `ability-score:<ability>`, `ability-modifier:<ability>`, `saving-throw:<ability>` | `strength` … `charisma` |
| `proficiency-bonus`, `skill:<skill>` | `skill:sleight-of-hand` (the name in lower case, spaces as `-`) |
| `spell-attack:<spellcasting>`, `spell-save-dc:<spellcasting>` | `spell-attack:wizard` (the spellcasting's name as a slug) |
| `statistic:<name>` | any statistic of `GET …/statistics`: `statistic:ac:misc`, `statistic:innate-speed` |

The web app works out armor class, hit points, initiative, speeds, passive Perception and attacks itself (from these
figures and the inventory; see [Attacks](#attacks)). For their keys (`armor-class`, `hit-points`, `initiative`,
`passive-perception`, `speed:walk|fly|climb|swim`, `attack:<inventory or custom attack id>:to-hit|damage`) the
server answers `"status": "unsupported"` rather than a trace of its own; the web app traces them in the same shape
(`src/frontend/apps/builder-app/src/lib/sheet-figures.ts`), and its "Why this value?" buttons on the interactive
sheet show both.

The answer:

- `status`: `traced`, `missing` (a value the server traces, but this character has no such spellcasting or
  statistic; `reason` says which) or `unsupported` (above).
- `trace`: `key`, `label`, `final` (the figure), `steps`, `excluded`, `notes`. Replaying the steps from the first
  gives `final`. Each step has an `operation` (`start`, `add`, `replace`, `maximum`, `minimum`, `multiply`, `divide`,
  `round-down`, `round-up`, `round-toward-zero`), its operand `value`, the running `result`, a `label`, a `source`
  and, when the operand is a value of its own, its trace as `input`.
- `source.kind`: `rule` (a statistic rule of one of the character's elements: element, registration, rule ids and
  Aurora's `bonus` name), `user-supplied` (what the player entered: a base score, rolled hit points, an attack written
  by hand; shown as entered, not worked out), `character` (a level), `fixed` (a number of the rules, the 8 of a save
  DC), `calculated` (another value; `refers` names its statistic).
- `excluded`: what was weighed and left out, with a `reason`: `stacking` (a bonus of the same kind counts once),
  `lower-alternative`, `set-not-higher` (an item setting a score no higher than it is), `condition-not-met`,
  `unresolved-dependency` (it refers to a statistic nothing gives). Only what the calculation actually weighed is
  listed: requirements, levels and "while wearing" conditions are applied before the figures are calculated, so the
  trace shows their result (a rule that is not there).
- `content`: the `sources` and rules `editions` of the elements in the trace, and a `fingerprint` of their rules
  (the same content gives the same fingerprint on any server; ids are left out).

Errors are `{ contractVersion, code, message }`: `400 invalid-argument` (no key, or not a key above),
`400 unsupported-version`, `404 not-found`. The contract is version 1 (`contractVersion` in every answer). A change
that is not backwards compatible raises it, and a request for a version the server does not have is refused with
`unsupported-version`; new fields can appear without a new version. TypeScript types generated from `openapi.json`:
`src/frontend/apps/builder-app/src/lib/contracts/calculation-trace.ts`.

**Worked example** ([`examples/calculation-trace.json`](examples/calculation-trace.json), a real answer for the
reference bard c04 of `src/frontend/apps/builder-app/reference-characters`): spell attack +6.

```bash
curl -s 'http://localhost:8093/api/characters/<id>/calculation/trace?value=spell-attack:ref-bard&version=1' \
  | jq '.trace.steps[] | [.operation, .value, .result, .label]'
```

| Step | Operand | Result | Source, and its own trace (`input`) |
|---|---|---|---|
| start: Proficiency bonus | 2 | 2 | `proficiency-bonus`: the Proficiency Bonus rule, +2 at level 2 |
| add: Charisma modifier | 3 | 5 | `ability-modifier:charisma`: score 16, add −10, divide by 2, round down |
| add: Spell attack bonuses | 1 | 6 | `statistic:spellcasting:attack`: Ref Rod of Focus +1 |

## Writing

Writes replace the whole document: read it, change it, send it all back.

- **Inventory:** `PUT …/inventory` with the `revision` you read. The answer is `{ revision }` (the new one); `409` when
  someone saved in between: read again and reapply. Leave `customAttacks` out (or `null`) to keep the stored ones;
  send `[]` to remove them all. Preserve `edited` on custom attacks; API edits should set it to `true`.
  `removedAttackIds` is optional (omitted/null preserves stored IDs; `[]` clears the suppression list),
  with at most 1000 nonblank identifiers of at most 64 characters. To keep a deletion across reimports,
  remove the row and add its ID to this list in the same revision-checked save. Server-owned fields (`registrationId`) are ignored.
- **Combat:** `PUT …/combat` with the `received` value you read; `409` with the current state when the character was
  given something in between (a potion from a party member).
- **Magic:** `PUT …/magic`; the last write wins.

Changing the build (classes, choices, ability scores) goes through the builder session
(`/api/characters/{id}/builder/session` with save and discard), as the web app's builder does. Plan on reading
there.

## Imports and homebrew (admin)

These need the admin key (`X-Admin-Key`) or the admin token, like every write under `/api/elements`.

**What an import ran into.** The Aurora import (`POST /api/elements/import-aurora`), a homebrew file's upload and
removal (`POST`/`DELETE /api/elements/homebrew`), and the content update's job status
(`GET /api/admin/content-sources`, its `job`) carry `diagnostics`. They use the same `ImportDiagnostic` shape:

```json
{ "severity": "warning", "code": "grant-unresolved", "importer": "homebrew", "file": "homebrew/my-feats.xml",
  "elementId": "ID_BREW_FEAT", "elementName": "Brew Feat", "detail": "ID_GONE",
  "message": "Brew Feat grants ID_GONE, which is not in the content; that grant was left out." }
```

- `severity`: `error` (nothing was saved), `warning` or `info`.
- `importer`: `aurora`, `5etools`, `homebrew` or `aurora-character`.
- `code`: `unreadable-file`, `index-entry-missing`, `append-target-missing`, `grant-unresolved`,
  `class-feature-missing`, `subclass-class-missing`, `duplicate-id`, `element-incomplete`, `requirement-unsupported`,
  `text-only`, `entries-skipped`, `invalid-value`, `replaces-existing` or `stale-revision`. The web app's Aurora
  character import also uses `element-missing` and `choice-unmatched`.
  - `duplicate-id`: two files define the id. `detail` is the file whose copy is used, `file` the one left out; `info`
    when homebrew or a pull request overrides on purpose, `warning` between equals.
  - `requirement-unsupported`: a requirement names an id that is not in the content, or a term Starfall cannot check
    (`detail`). It never holds, so with `!` before it, it always does.
  - `text-only` (`info`): a feature, feat or trait with no rules. Its description is shown; nothing is applied.
  - `entries-skipped` (`info`, 5etools): entries left out for one reason (`detail`: `unearthed-arcana`, `reprinted`,
    `not-for-players` or `aurora-has-it`), how many in `count`.
- New codes can appear; show an unknown one by its `message`.
- Only what the import itself ran into is listed. They come sorted, at most 50 of a code; the last one listed says in
  `omitted` how many more there were.
- `entries` counts what the import did with its entries, each once: `complete` (imported in full), `partial` (a grant,
  a requirement or a feature it refers to was left out), `textOnly`, and `skipped` (not imported: an incomplete
  element, the copy of a repeated id that is not used, 5etools entries left out). The categories follow Borealis's
  import report (converted, partial, skipped).
- The text `warnings` and the counts are still there, as before.

**Homebrew page edits: preview, then save.** Reads of spells, subclasses and classes
(`GET /api/elements/homebrew/{spells,subclasses,classes}`) give each record a `revision`.

- `POST …/{kind}/preview` with the form as the `PUT` would send it saves nothing. It answers with what the save would
  do:
  - `changes`: each field, before and after;
  - `elements`: elements added, changed or removed;
  - `characters`: who uses the record (`uses`), what each would lose (`loses`), and the picks they keep that would no
    longer fit (`invalidated`: a spell prepared as a class's spell leaving that list, a subclass moved to another
    class's choice, a class's subclass key changing under the Homebrew page's subclasses picked for it);
  - `diagnostics`: the save's own checks.
- `PUT` with `expectedRevision` set to the revision you read or previewed is refused with `409` and a fresh preview
  (`stale-revision`) when the record changed or was removed since. Without `expectedRevision`, the save works as
  before.

## Examples

```bash
curl -s http://localhost:8093/api/characters | jq '.characters[] | {characterId, name, level}'
curl -s http://localhost:8093/api/characters/<id>/inventory | jq '.customAttacks'

# a locked player's character
TOKEN=$(curl -s -X POST http://localhost:8093/api/characters/players/unlock \
  -H 'Content-Type: application/json' -d '{"name":"Ilyra","password":"…"}' | jq -r .token)
curl -s -H "X-Player-Token: $TOKEN" http://localhost:8093/api/characters/<id>/inventory

# how a figure was worked out
curl -s 'http://localhost:8093/api/characters/<id>/calculation/trace?value=skill:stealth' | jq '.trace'
```

```bash
python3 docs/api/examples/starlights_client.py http://localhost:8093 [--player NAME] [--json]
node docs/api/examples/starlights-client.ts http://localhost:8093   # Node 22.18+; STARLIGHTS_PLAYER/STARLIGHTS_PASSWORD
```

Generated clients: `npx openapi-typescript docs/api/openapi.json -o starlights.d.ts`, or
`openapi-python-client generate --path docs/api/openapi.json`.

## Limitations

- **Weapon attack figures are not in the API** (see [Attacks](#attacks)): a client gets the weapon, its dice and the
  character's modifiers, and adds them itself, or shows the custom attacks and the weapon names. A consolidated
  "character sheet" endpoint would remove this; it does not exist yet.
- **Access is all or nothing per player.** No read-only tokens, no scopes; anyone who can reach the server can read
  an unlocked player's characters and change them.
- **Query parameters** declared with FastEndpoints' `[QueryParam]` (like `player` on the list, `value` and `version`
  of a calculation trace) are not in `openapi.json`; this guide names them.
- **Calculation traces** cover what the server calculates; the sheet's own figures are traced by the web app, not
  the API (see [Calculation traces](#calculation-traces)).
- **Import diagnostics** report what the importers can see: a 5etools data file that is not valid JSON still stops
  the import as an error (skipping it could re-create generated classes without their features), and
  `skippedConditionalRules` is always 0 (conditions are kept as requirements), so it has no diagnostic. Which characters use a homebrew record is found from their
  build choices and prepared spells; a preview loads every character to look at the prepared spells. A spell's
  `invalidated` pick compares the spellcasting it is prepared as with the list's name (a class whose list has another
  name is not matched), and a class's new subclass key is checked against the Homebrew page's subclasses only (not
  uploaded files'). `text-only` is decided by the element alone: a feature whose effect another element's rules
  carry is still reported.
- **Content administration** (`/api/elements` writes, `/api/admin`) is in the description for completeness but needs
  the admin key. Some of those endpoints answer `201 Created` where the description says `200`.
- The API has no versioning beyond this repository: a new Starfall version can add fields. Ignore fields you don't
  know, and compare `openapi.json` between versions.

## Keeping it current

`openapi.json` is written by an integration test from the running endpoints (in-memory database, no server
needed), and the same test fails when the endpoints change and the file does not:

```bash
# regenerate (from the repository root)
STARLIGHTS_UPDATE_OPENAPI=1 dotnet test --project src/tests/integration/Starlights.Integration.Tests --filter FullyQualifiedName~OpenApiDocument
# check (part of the normal test run; the GitHub workflow runs it when started by hand)
dotnet test --project src/tests/integration/Starlights.Integration.Tests --filter FullyQualifiedName~OpenApiDocument
```

The `--project` option follows the .NET 10 Microsoft.Testing.Platform runner selected in `global.json`.

In development (`dotnet run` / Aspire) the server also serves it live at `/openapi/v1.json` with Scalar at
`/scalar`.
