# Looking after your Starfall server: backups, restoring and updating

This is for anyone running Starfall with Docker from the `deploy/` folder (the install `setup.sh` makes). You don't
need to know SQL Server or Docker well: every command below is copied as-is into a terminal **in the `deploy/`
folder** of your Starfall checkout.

Nothing here runs by itself. Updates and restores only happen when you run them, and they ask first.

## What your group's data is

| What | Where | In a backup? |
|---|---|---|
| Characters, campaigns, players, imported content, homebrew as the app uses it | the SQL Server database (`data/mssql`) | yes, as a proper SQL Server backup |
| Portraits and campaign pictures | `data/portraits` | yes |
| Homebrew files (uploaded `.xml`, and what the Homebrew page's forms write) | `data/homebrew` | yes |
| Background pictures you added (home page, Homebrew tile, …) | `data/art` | yes |
| Downloaded books (Aurora Legacy, 5etools) and the Compendium of Lore | `data/content`, `data/lore` | no: the Content page downloads them again |
| Your passwords and keys | `.env` | only with `--include-env` (not needed to restore) |

Never back up by copying `data/mssql` while the site runs: SQL Server's files are only consistent inside SQL
Server, and a copy made while it writes can be broken without you noticing. The scripts below use SQL Server's own
backup, which it checks.

## Making a backup

```bash
maintenance/backup.sh
```

It takes about a minute and the site keeps running. You get a folder like `data/backups/20261016-031500/` with:

- `database.bak`: the database, checked by SQL Server after it was written
- `files.tar.gz`: portraits, homebrew and art
- `manifest.json`: when it was made, from which version, and how many characters, campaigns and players are in it
- `SHA256SUMS`: fingerprints that show if the backup is damaged later

Give a backup a name with `--label before-big-change`. The backup folder and its files can only be read by your
user. **Copy the backup folder to another disk or computer** (a USB disk, another machine, cloud storage): a backup
on the same disk as the server does not survive that disk dying.

Back up when nobody is editing homebrew: the database and the homebrew files are saved a few seconds apart.

If another Compose file runs the site (a homelab's own, outside `deploy/`) and keeps its data in this `deploy/data`,
`backup.sh` finds that Compose project and backs it up, as long as `deploy/` has no database container of its own.
Restore, update and rollback only work on a site run from `deploy/`: with another project they stop and say so,
rather than start a second SQL Server on the same data folder.

### Checking a backup

```bash
maintenance/verify.sh                      # the newest backup: files intact?
maintenance/verify.sh data/backups/<folder> --deep
```

`--deep` proves the backup works: it restores it into a separate, temporary SQL Server (it never touches your
site), runs SQL Server's full consistency check, and compares the numbers of characters, campaigns and players with
what the backup says it holds. Do this now and then, and always for a backup you copy away for safekeeping.

### Backing up every night

If you want one, add a nightly backup yourself (`crontab -e`):

```
15 4 * * * cd /path/to/starlights/deploy && maintenance/backup.sh --label nightly >> data/backups/backup.log 2>&1
```

Backups are never deleted automatically: remove old folders from `data/backups` yourself when the disk fills.

## Restoring a backup

Restoring **replaces** everything in the site (database, portraits, homebrew, art) with what is in the backup.

```bash
maintenance/restore.sh data/backups/<folder>
```

What it does, in order (it stops at the first problem, with a message saying what to do):

1. Checks the backup fully (including the temporary test restore).
2. Refuses a backup made by a **newer** version of Starfall or of SQL Server: update the code first.
3. Shows what is in the backup and what is there now, and asks you to type the install's name.
4. Makes a **safety backup** of the current state (label `pre-restore`), so a restore can itself be undone.
5. Stops the site, restores the database, moves the current portraits/homebrew/art aside to
   `data/.before-restore-<time>/` (nothing is deleted) and unpacks the backup's folders.
6. Starts the site again (a backup from an older version is brought up to date automatically) and checks that the
   numbers of characters, campaigns, players and files match the backup.

Players need to unlock their characters again only if you restore onto a new server with a different `.env`.

### Moving to a new computer

1. Install Starfall on the new computer with `setup.sh` (see [installation.md](installation.md)). To keep the same
   admin password, put your old `.env` there **before** running setup: `cp env.backup deploy/.env && chmod 600
   deploy/.env` (from a backup made with `--include-env`; setup refuses a `.env` other users can read).
2. Copy the backup folder over, into its `deploy/data/backups/` (after setup: setup refuses to start on a `data/`
   folder that already has files but no `.env`).
3. `maintenance/restore.sh data/backups/<folder>`.

## Updating to a new version

```bash
maintenance/update.sh --check     # what would change (changes nothing)
maintenance/update.sh             # the update itself
```

`--check` lists the new changes and what they touch:

- **web app only**: the pages change, your data is not touched.
- **server**: the program behind the site changes.
- **DATABASE CHANGES**: the new version changes the database's structure when it starts. Once that has happened,
  the old version can't run on it any more; going back means restoring the backup the update makes.
- **content importer**: the release notes may ask you to import the books again (`./admin.sh import … --replace`),
  while nobody plays.

The update itself:

1. checks: no local changes to the code, the new version continues yours, enough disk, and the site works now
2. asks you to type the install's name
3. makes a backup (and stops if it fails)
4. keeps the running version's images as `:rollback-<old version>`
5. moves the code and builds the new version; **the site keeps running the old version until the build works**
6. starts the new version: database changes first, then the API, then the web app
7. checks the site answers, runs the new version, and that no characters, campaigns, players, portraits or homebrew
   files went missing

If anything fails it stops and prints exactly what to do. Players with the site open should reload it once.

Each install has its own images, named after its Compose project (`COMPOSE_PROJECT_NAME` in `.env`, which
`setup.sh` makes unique), and its rollback copies are tagged on those names. So two installs on one computer (say a
test copy next to your real one) never build over each other or roll back into each other's version. An install
whose `.env` has no `COMPOSE_PROJECT_NAME` (made before the current `setup.sh`) is named after its folder,
`deploy`: its images become `deploy/backend` and `deploy/web` with this version (before, every install shared
`starlights/backend` and `starlights/web`; `update.sh` keeps the rollback copy under both names, and the old
`starlights/*` images can be removed once you are happy with the update). Two such installs both in folders named
`deploy` would be the same Compose project: give each its own `COMPOSE_PROJECT_NAME` in `.env`.

## Going back after an update

There are two very different kinds of "going back":

| | When | What it changes | Command |
|---|---|---|---|
| **Program rollback** | the update had **no database changes**, and the new version misbehaves | the web app and API go back to the kept images; your data stays as it is now (nothing typed in since the update is lost) | `maintenance/rollback.sh <old version>` |
| **Web-only rollback** | only the pages are wrong | the web app only | `maintenance/rollback.sh <old version> --web-only` |
| **Full restore** | the update **had database changes**, or data is wrong | program and data go back to the moment before the update; **everything entered since then is lost** | `maintenance/rollback.sh <old version> --after-restore`, then `maintenance/restore.sh data/backups/<the pre-update backup>` |

`rollback.sh` refuses a program rollback when the database already has changes the old version doesn't know (the
old program on a newer database could break or damage data), and tells you to do the full restore instead.
`update.sh` prints the `<old version>` and the backup folder when it finishes; `data/maintenance/last-update.json`
remembers them too.

## When something goes wrong

| Message | What to do |
|---|---|
| `the database container is not running` | `docker compose up -d starlights-db`, wait a minute, run the command again |
| `not enough free disk` | delete old folders in `data/backups` (after copying them away), or `docker image prune` |
| `the code has local changes` | someone edited files in the checkout: `git status` shows them; commit or undo them |
| `is not a straight continuation of your code` | your checkout has its own commits: ask for help before updating |
| `the new version did not build` | nothing live changed; the message shows the two commands that put the code back |
| `the database step … did not finish` | look at its log (the message shows the command); the backup from just before is listed |
| `this backup comes from a NEWER version` | update the code first (`maintenance/update.sh`), then restore |
| `some files do not match their checksums` | that backup is damaged: use another one (your off-machine copy) |

Everything a script printed is worth keeping when you ask for help; it never prints your passwords.

## Notes for the homelab install

The homelab server runs Starfall from `~/homelab/compose/starlights` with fixed container names and its own
folders (`~/homelab/config/starlights/...`), not from `deploy/`. These scripts are for the `deploy/` layout; the
homelab keeps its own routine (`starlights-deploy-web.sh`, the verified pre-deploy backups) until it moves to it.
