# Using Starfall

The day-to-day guide for players and the DM, the settings in `deploy/.env`, and how the app gets its content. To
install Starfall see the [README](../README.md#quick-install) and [installation.md](installation.md); for backups and
updates see [maintenance.md](maintenance.md).

## Players

1. Open the site and **pick your name** (top right). There are no accounts: your characters are filed under your name.
   To stop others changing them, use **Lock your characters** to give your name a password.
2. **Create Character**. Pick the rules (2014, 2024 or both) and leave "Guide me step by step" ticked if you're new:
   the wizard walks through class, species, background, ability scores, the books' starting equipment and spells.
   Experienced players untick it and go straight to the full builder (tabs: Build, Abilities, Magic, Equipment, Story,
   Sources). Already have an Aurora character? Use **Import from Aurora** with its `.dnd5e` file.
3. Changes are kept only when you press **Save** in the bar at the bottom (or **Discard changes** to go back).
4. **Character sheet** shows the sheet and downloads it as a PDF. **Battle** opens the Battle Action Simulator for
   playing: hit points, attacks, spells and slots, conditions, rests.
5. Join your group's campaign with the **Campaigns** button on your character (the DM may give you a password).

**Theme and units.** The palette button in the header picks the theme (Porcelain, Slate or Midnight, or follow the
device) and the measurement units: **Imperial** (feet, pounds; the default) or **Metric** (metres, kilograms, using the
table convention 5 ft = 1.5 m, 1 lb = 0.5 kg, 1 mile = 1.5 km). Both are kept per browser. Units change only what you
see: rules text, the PDF sheet, Aurora files and everything saved stay in feet and pounds.

## The DM

1. Press the **key button** in the header and enter the admin password. It opens every character and campaign, and
   allows homebrew. (Anyone with it is a DM, so keep it to yourself.)
2. **Campaigns → New campaign.** In **Campaign settings** choose the party, add a cover, set a password for players
   and a DM password (so a co-DM can run it without the admin password), and switch on **Use homebrew** if you want it.
3. Everything you add starts hidden: players see an entry only when you reveal it. Use the **DM / Player** switch at the
   top to check what players see.
4. **Encounters → Run the fight**: add the party and the monsters, roll initiative, track hit points; players follow
   along in their simulators.
5. **Magic items → Give to…** puts an item straight into a character's equipment and notes it in the Gold tab.
6. **Campaign settings → Export campaign** saves the whole campaign as one `.zip` (see below).

### Exporting a campaign

The DM (the campaign's DM password or the admin) can download a campaign as one `.zip` file to keep or read
elsewhere. It holds `campaign.json` and a `pictures/` folder:

| Included | Left out on purpose |
|---|---|
| the campaign's name, description, cover, party, DM name and settings (Use homebrew; whether it has passwords) | the campaign's password, the DM password and players' passwords (no hashes either) |
| every entry as the DM sees it: sessions, codex, quests, maps, handouts, notes, DM notes, links (DM-only ones too) | players' **private** notes (only their authors can read them, not even the DM) |
| the ledger (Gold tab: party fund and stash lines) and the magic items with where each one is | unlock tokens, the admin key and every server setting |
| encounters with their running fights (initiative, monster hit points, conditions) | the books' content (Aurora Legacy, 5etools): ids and links point into it |
| each party member: all of their build (the same capture as the builder's Save/Discard), equipment, spells, story and fight state; who carries which magic item | the build of a character whose player locked it, unless the DM holds that player's unlock token (then only name and player) |
| the books' and homebrew items the campaign's magic items are, by id, name and book | homebrew files themselves (back up `data/homebrew`), other campaigns and characters |
| the campaign's pictures and the party's portraits and story pictures | |

The export holds your DM notes: keep it private. Starfall can't import it yet; it is a readable, complete copy
(`campaign.json` maps each picture to the address the campaign uses). For a full backup of the server, see
[maintenance.md](maintenance.md).

## Homebrew

Aurora XML uploads can contain new `<element>` definitions, `<append id="…">` patches, or both, inside one
`<elements>` root. Append-only files are supported. The Files tab lists patch target IDs and warns when their
source content is missing. Install that content and upload the patch again to apply it. Uploading the same
filename replaces the patch; removing the file removes its additions while retaining other files’ patches.
Patches modify the target's original source content, so they apply to characters using that source rather than
creating a separate Homebrew book.

On the **Homebrew** page (admin password needed) make magic items, monsters, spells, subclasses and classes with forms, or upload Aurora element
files (`.xml`) for classes, species, backgrounds and anything else. Homebrew starts switched off everywhere:

- a player ticks the homebrew book in their character's **Sources** tab to use it in the builder;
- a DM ticks **Use homebrew** in the campaign settings to hand out homebrew items and add homebrew monsters to fights.

## Settings

The settings live in `deploy/.env` (never share or commit it):

| Setting | What it is |
|---|---|
| `STARLIGHTS_MASTER_PASSWORD` | The admin password (key button). Change it any time, then `docker compose up -d`. |
| `STARLIGHTS_PORT` | The port the site is on (default `8093`). Change it, then `docker compose up -d`. |
| `STARLIGHTS_ADMIN_KEY` | Sent by `./admin.sh` for imports. Any long random text. |
| `STARLIGHTS_SA_PASSWORD` | The database password. Set once by `setup.sh`; **don't change it** after the first start (the database keeps the old one). |

## How Aurora Legacy and 5etools are connected

The app brings in its own content from two GitHub links, kept on its **Content** page (in the top bar once you unlock
admin with the key button):

| Content | Standard link | Used for |
|---|---|---|
| Aurora Legacy | [github.com/AuroraLegacy/elements](https://github.com/AuroraLegacy/elements) | classes, species, backgrounds, feats, spells, items: the builder's rules |
| 5etools | [github.com/5etools-mirror-3/5etools-src](https://github.com/5etools-mirror-3/5etools-src) | the Compendium of Lore, deities and rules Aurora lacks, creatures for encounters, starting equipment |

To use your own fork (or a branch, as `…/tree/branch-name`), paste its link and press **Save and update now**. The
server then:

1. downloads each repository at its newest commit (skipped when nothing changed; for 5etools only its `data` folder);
2. sets up the base rules the first time;
3. imports Aurora Legacy, what 5etools has and Aurora Legacy lacks, and the built-in extras;
4. builds the Compendium of Lore;
5. runs every character through the rules again, so they get new choices.

**When 5etools is ahead of Aurora Legacy.** A new book often reaches 5etools first. Whatever 5etools has and Aurora
Legacy (with its open pull requests and your homebrew) does not (spells, feats, class options such as invocations,
backgrounds, species, subclasses, classes) is translated into Aurora's format and imported with everything else,
marked "Imported automatically from 5etools". What 5etools records as data becomes rules: proficiencies and their
choices, ability scores, prerequisites, spell slots and prepared spells, spells a subclass always has prepared. What it
only describes in words stays words on the sheet. Aurora's version always wins: once Aurora Legacy adds the book, its
own elements are used and the translation stops (a character who already took the translated one keeps it). To turn
this off, set `FiveETools__Generate=false` for the API in `deploy/docker-compose.yml`.

The page also shows where things stand: the **server** (the Starfall version it was built from, whether every
database change is applied, and how many characters, campaigns, elements and homebrew items there are) and the
**content** (Aurora Legacy's commit, the 5etools release, the last update that worked, and whether one is running).
**Check for updates** asks GitHub, only when you press it, what is newer: Aurora Legacy's new commits and the files
they change, 5etools' new release and its notes, and what **Update now** would download. Nothing updates by itself.

Only GitHub links are accepted. The downloads live in `deploy/data/content`, the Compendium in `deploy/data/lore`, and
your homebrew (`deploy/data/homebrew`) is never touched. The character sheet needs nothing extra: its templates and
fonts are part of the app. An optional extra:

- **Background pictures**: the app comes with its own. To use yours, put `lore.webp`, `battle.webp` or `homebrew.webp`
  in `deploy/data/art`; a picture there is used instead of the one with the same name.

Installed before the sheet was built in? The `deploy/data/aurora-sheets` folder is no longer used and can be deleted.

### Content from the command line

New content: press **Save and update now** on the Content page, or run `./admin.sh update` in `deploy/`. The database
changes a new version needs are applied by themselves when it starts. To get new content every night, add a cron job
(`crontab -e`):

```
30 3 * * * cd /path/to/starfall/deploy && ./admin.sh update >> data/update.log 2>&1
```

`./admin.sh` (run it without arguments for help):

| Command | What it does |
|---|---|
| `./admin.sh update [--force]` | the same as the Content page's button: downloads what changed, imports it, rebuilds the Compendium, updates characters (`--force`: everything again) |
| `./admin.sh status` | what the content update is doing or last did |
| `./admin.sh import <index> [--update\|--replace]` | imports one index again: `AuroraLegacy.index`, `5etools`, `starlights` (built-in extras), `homebrew` |
| `./admin.sh reprocess` | runs every character through the rules again |
