#!/usr/bin/env bash
# Backs up a Starfall install while it runs: the database with SQL Server's own BACKUP (checksummed, verified),
# plus the portraits, campaign pictures, homebrew files and artwork in deploy/data.
#
#   deploy/maintenance/backup.sh [--label NAME] [--include-env]
#
# Writes deploy/data/backups/<date>-<time>[-NAME]/ (only you can read it):
#   database.bak       the SQL Server backup (copy-only, with checksums; RESTORE VERIFYONLY passed)
#   files.tar.gz       portraits/, homebrew/, art/ exactly as they were (owners kept)
#   manifest.json      when, which code and SQL Server version, the database's migrations, counts of characters,
#                      campaigns, players and content, and the folders' fingerprints
#   SHA256SUMS         checksums of the files above
#   env.backup         only with --include-env: a copy of deploy/.env (passwords!). Not needed to restore.
# A backup that did not finish stays in a folder ending in .incomplete and is never used by restore.
# Copy finished backups to another disk or computer: a backup on the same disk does not survive a broken disk.
source "$(dirname "$0")/lib.sh"

label="" include_env=false
while [ $# -gt 0 ]; do
  case "$1" in
    --label) label="${2:?--label needs a name}"; shift 2 ;;
    --include-env) include_env=true; shift ;;
    -h|--help) sed -n '2,19p' "$0"; exit 0 ;;
    *) die "unknown option: $1" "Run with --help to see the options." ;;
  esac
done
label=$(printf '%s' "$label" | tr -c 'A-Za-z0-9._-' '-')

require_install
use_compose_for_data_mount
step "Checking the install"
require_db_running
db_exists || die "the database '$DB_NAME' does not exist yet." "Start the whole site once (docker compose up -d) so it is created."
project=$(project_name)
mkdir -p "$BACKUP_ROOT"; chmod 700 "$BACKUP_ROOT"
size=$(db_size_mb); free_backup=$(free_mb "$BACKUP_ROOT"); free_data=$(free_mb "$DATA_DIR")
say "install: $project   database: ${size} MB   free: ${free_backup} MB (backups), ${free_data} MB (database disk)"
# the backup is written next to the database first, then copied out: room for it twice (plus the files)
[ "$free_data" -gt $((size + 200)) ] || die "not enough free disk next to the database (${free_data} MB free, need about $((size + 200)) MB)." "Free some space (old backups in $BACKUP_ROOT?) and try again."
[ "$free_backup" -gt $((size + 500)) ] || die "not enough free disk for the backup (${free_backup} MB free, need about $((size + 500)) MB)." "Free some space or set STARLIGHTS_BACKUP_DIR to another disk."

stamp=$(date +%Y%m%d-%H%M%S)
name="$stamp${label:+-$label}"
work="$BACKUP_ROOT/$name.incomplete"
mkdir "$work"
inner="$CONTAINER_BACKUP_DIR/starlights-maint-$name.bak"
cleanup_inner() { dc exec -T starlights-db rm -f "$inner" >/dev/null 2>&1 || true; }
trap 'cleanup_inner; printf "\nThe backup did not finish; what was written is in %s (not usable).\n" "$work" >&2' ERR

step "Backing up the database (SQL Server BACKUP, with checksums)"
dc exec -T starlights-db mkdir -p "$CONTAINER_BACKUP_DIR"
printf "BACKUP DATABASE [%s] TO DISK = N'%s' WITH COPY_ONLY, CHECKSUM, INIT, FORMAT, NAME = N'Starfall %s';\nGO\n" "$DB_NAME" "$inner" "$name" | sql \
  || die "SQL Server could not back up the database (see the message above)." "Nothing was changed. Common causes: a full disk, or the database container restarting."
printf "RESTORE VERIFYONLY FROM DISK = N'%s' WITH CHECKSUM;\nGO\n" "$inner" | sql \
  || die "the new backup did not pass SQL Server's check (RESTORE VERIFYONLY)." "Do not rely on it. Check the disk (df -h, dmesg) and run the backup again."

step "Recording what is in it"
migrations=$(db_migrations)
counts=$(db_counts)
sqlver=$(sql_server_version)
dc cp "starlights-db:$inner" "$work/database.bak" >/dev/null
cleanup_inner
chmod 600 "$work/database.bak"

step "Backing up portraits, homebrew and artwork"
uid=$(id -u); gid=$(id -g)
docker run --rm --network none --security-opt no-new-privileges -v "$DATA_DIR:/d:ro" -v "$work:/out" alpine:3.21 sh -c '
  set -e; cd /d; dirs=""
  for f in '"${USER_FOLDERS[*]}"'; do [ -d "$f" ] && dirs="$dirs $f"; done
  tar czf /out/files.tar.gz --numeric-owner $dirs
  chown '"$uid:$gid"' /out/files.tar.gz && chmod 600 /out/files.tar.gz'
fingerprints=$(folder_fingerprints)
if $include_env; then cp "$DEPLOY_DIR/.env" "$work/env.backup"; chmod 600 "$work/env.backup"; fi

commit=$(git -C "$REPO_DIR" rev-parse --short HEAD 2>/dev/null || echo unknown)
running=$(api_commit)
python3 - "$work/manifest.json" <<PY
import json, sys
def rows(text): return [l.split("|") for l in text.splitlines() if l.strip()]
manifest = {
  "format": 1,
  "created": "$(date -Iseconds)",
  "label": "$label",
  "project": "$project",
  "code_commit": "$commit",
  "running_api_commit": "$running",
  "sql_server_version": "$sqlver",
  "database": "$DB_NAME",
  "database_size_mb": int("$size" or 0),
  "migrations": [m for m in """$migrations""".split() if m],
  "counts": {k: int(v) for k, v in rows("""$counts""")},
  "folders": {f: {"files": int(n), "fingerprint": h} for f, n, h in rows("""$fingerprints""")},
  "includes_env": "$include_env" == "true",
}
json.dump(manifest, open(sys.argv[1], "w"), indent=2)
PY
chmod 600 "$work/manifest.json"
parts=(database.bak files.tar.gz manifest.json); [ -f "$work/env.backup" ] && parts+=(env.backup)
(cd "$work" && sha256sum "${parts[@]}" > SHA256SUMS && chmod 600 SHA256SUMS && sha256sum -c --quiet SHA256SUMS) \
  || die "the files written do not match their checksums." "The disk may be failing; do not rely on this backup."

trap - ERR
final="$BACKUP_ROOT/$name"
mv "$work" "$final"
chmod 700 "$final"
step "Backup finished: $final"
python3 - "$final/manifest.json" <<'PY'
import json, sys
m = json.load(open(sys.argv[1]))
c = m["counts"]
print(f"  characters {c.get('characters')}, campaigns {c.get('campaigns')}, players {c.get('players')}, content elements {c.get('elements')}")
print("  " + ", ".join(f"{k}: {v['files']} files" for k, v in m["folders"].items()))
print(f"  code {m['code_commit']}, SQL Server {m['sql_server_version']}, {len(m['migrations'])} migrations")
PY
du -sh "$final" | awk '{print "  size " $1}'
say "Copy this folder to another disk or computer too. Check it any time with: deploy/maintenance/verify.sh $final"
