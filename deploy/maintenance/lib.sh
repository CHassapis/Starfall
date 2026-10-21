# shellcheck shell=bash disable=SC2034
# Shared helpers for the Starfall maintenance scripts (backup, verify, restore, update, rollback).
# Sourced, not run. Works on the install in deploy/ (docker-compose.yml + .env next to it), whatever its compose
# project name. backup.sh also works when another Compose file (e.g. a homelab's) runs this install's database from
# deploy/data/mssql. Secrets are never printed or put on a command line: SQL runs inside the database container with
# the password that container already has.

set -Eeuo pipefail
umask 077
# a command that fails where no message was planned still says where it stopped (scripts may set their own trap)
trap 'printf "\nSTOPPED unexpectedly at line %s of %s (exit %s). Nothing after that point was done; the messages above show what failed.\n" "$LINENO" "$(basename "$0")" "$?" >&2' ERR

MAINT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "$MAINT_DIR/.." && pwd)"
REPO_DIR="$(cd "$DEPLOY_DIR/.." && pwd)"
DATA_DIR="$DEPLOY_DIR/data"
BACKUP_ROOT="${STARLIGHTS_BACKUP_DIR:-$DATA_DIR/backups}"
DB_NAME="starlights"
# the folders of user data next to the database (content and lore are downloaded/rebuilt by the app)
USER_FOLDERS=(portraits homebrew art)
# inside the database container; it is part of the data/mssql bind mount
CONTAINER_BACKUP_DIR="/var/opt/mssql/backup"

# where the script was run from: a folder named on its command line is relative to that, not to deploy/
CALLER_DIR="$PWD"
cd "$DEPLOY_DIR"

say()  { printf '%s\n' "$*"; }
# a folder named on the command line, as an absolute path: from where the script was run, else from deploy/ (so
# "data/backups/<folder>" works from the deploy folder as the docs say, and from anywhere else too)
user_dir() {
  local dir="$1"
  case "$dir" in /*) ;; *) [ -d "$CALLER_DIR/$dir" ] && dir="$CALLER_DIR/$dir" ;; esac
  (cd "$dir" 2>/dev/null && pwd)
}
step() { printf '\n== %s\n' "$*"; }
warn() { printf 'WARNING: %s\n' "$*" >&2; }
die()  { printf '\nSTOPPED: %s\n' "$1" >&2; [ -n "${2:-}" ] && printf '%s\n' "$2" >&2; exit "${3:-1}"; }

need() { command -v "$1" >/dev/null 2>&1 || die "this needs '$1', which is not installed." "Install it and run this again."; }

# the Compose project the scripts work on: deploy/'s own, unless use_compose_for_data_mount picked another one
COMPOSE_ARGS=()
dc() { docker compose "${COMPOSE_ARGS[@]}" "$@"; }
# the same, as a command to show in a message
dc_shown() { printf 'docker compose'; [ "${#COMPOSE_ARGS[@]}" -eq 0 ] || printf ' %q' "${COMPOSE_ARGS[@]}"; }

# the database containers whose files are this install's data/mssql, one id per line, except those of the Compose
# project named in $1
data_mount_containers() {
  local id project source
  for id in $(docker ps -aq --filter label=com.docker.compose.service=starlights-db); do
    IFS='|' read -r project source < <(docker inspect --format '{{index .Config.Labels "com.docker.compose.project"}}|{{range .Mounts}}{{if eq .Destination "/var/opt/mssql"}}{{.Source}}{{end}}{{end}}' "$id" 2>/dev/null) || continue
    if [ -n "${1:-}" ] && [ "$project" = "$1" ]; then continue; fi
    if [ -n "$source" ] && [ "$source" -ef "$DATA_DIR/mssql" ]; then echo "$id"; fi
  done
}

# the given containers' Compose projects as "name (compose files)", on one line
compose_projects_of() {
  # shellcheck disable=SC2068
  docker inspect --format '{{with index .Config.Labels "com.docker.compose.project"}}Compose project {{.}}{{else}}container {{$.Name}}, not started by Compose{{end}}{{with index .Config.Labels "com.docker.compose.project.config_files"}} ({{.}}){{end}}' $@ | sort -u | paste -sd ';' -
}

# A homelab can run this install from its own Compose file outside deploy/, still mounting deploy/data. When deploy/'s
# own project has no database container and exactly one other one uses data/mssql, work on that one's project.
# Only backup.sh does: restore, update and rollback replace and rebuild things, so they stay with deploy/'s project
# (and require_db_running stops them).
use_compose_for_data_mount() {
  local ids project files dir file dest src folder
  local -a list=()
  [ -z "$(dc ps -aq starlights-db 2>/dev/null)" ] || return 0
  ids=$(data_mount_containers)
  [ -n "$ids" ] || return 0
  # shellcheck disable=SC2086
  [ "$(wc -l <<<"$ids")" -eq 1 ] || die "more than one database container uses this install's data folder ($DATA_DIR/mssql):
$(docker inspect --format '  {{.Name}} (Compose project {{index .Config.Labels "com.docker.compose.project"}}, {{.State.Status}})' $ids)" \
    "Two SQL Servers on one data folder damage it. Remove the one that should not be there, then back up."
  IFS='|' read -r project files dir < <(docker inspect --format '{{index .Config.Labels "com.docker.compose.project"}}|{{index .Config.Labels "com.docker.compose.project.config_files"}}|{{index .Config.Labels "com.docker.compose.project.working_dir"}}' "$ids")
  [ -n "$project" ] && [ -n "$files" ] && [ -n "$dir" ] \
    || die "the database container $(docker inspect --format '{{.Name}}' "$ids") uses this install's data folder, but does not say which Compose file started it." \
      "Back up with the tools of whatever started it, or run the database from deploy/ instead."
  [ -d "$dir" ] || die "the Compose folder that runs this install's database does not exist (any more): $dir"
  COMPOSE_ARGS=(--project-directory "$dir" -p "$project")
  IFS=',' read -r -a list <<<"$files"
  for file in "${list[@]}"; do
    [ -f "$file" ] || die "a Compose file that runs this install's database does not exist (any more): $file"
    COMPOSE_ARGS+=(-f "$file")
  done
  # the files part of the backup is read from deploy/data, so that project must keep portraits, homebrew and art there
  # shellcheck disable=SC2046
  while IFS='|' read -r dest src; do
    case "$dest" in /data/portraits) folder=portraits ;; /data/homebrew) folder=homebrew ;; /srv/art) folder=art ;; *) continue ;; esac
    [ "$src" -ef "$DATA_DIR/$folder" ] || die "the Compose project '$project' keeps $folder in $src, not in $DATA_DIR/$folder." \
      "This backup reads $folder from $DATA_DIR and would miss them. Back up with the tools of whatever runs '$project'."
  done < <(docker inspect --format '{{range .Mounts}}{{.Destination}}|{{.Source}}{{"\n"}}{{end}}' $(docker ps -aq --filter "label=com.docker.compose.project=$project"))
  say "The database runs in the Compose project '$project' ($files); using that."
}

# the compose project name (what `docker compose ls` shows): containers are <project>-<service>-1
project_name() {
  dc config --format json 2>/dev/null | python3 -c 'import json,sys; print(json.load(sys.stdin)["name"])'
}

require_install() {
  need docker; need python3; need sha256sum; need tar
  docker compose version >/dev/null 2>&1 || die "Docker's compose plugin is missing." "See https://docs.docker.com/compose/install/"
  [ -f "$DEPLOY_DIR/docker-compose.yml" ] || die "no docker-compose.yml in $DEPLOY_DIR."
  [ -f "$DEPLOY_DIR/.env" ] || die "no .env in $DEPLOY_DIR: this folder is not a Starfall install (run ./setup.sh first)."
}

# a value from deploy/.env as docker compose reads it: the last KEY=value line wins, an "export " prefix is allowed,
# Windows line endings (CRLF, e.g. after editing in Notepad) and surrounding quotes are dropped. Empty when absent.
env_value() {
  local line value
  line=$({ grep -E "^[[:space:]]*(export[[:space:]]+)?$1=" "$DEPLOY_DIR/.env" 2>/dev/null || true; } | tail -1)
  [ -n "$line" ] || return 0
  value=${line#*=}
  value=${value%$'\r'}
  if [ "${#value}" -ge 2 ]; then
    case "$value" in \"*\"|\'*\') value=${value:1:${#value}-2} ;; esac
  fi
  printf '%s' "$value"
}

site_port() { local p; p=$(env_value STARLIGHTS_PORT || true); echo "${p:-8093}"; }

# the image a service of this install uses, as docker-compose.yml names it (per Compose project, e.g.
# starlights-1a2b3c4d5e/backend; installs without COMPOSE_PROJECT_NAME: starlights/backend)
compose_image() {
  dc config --format json 2>/dev/null | python3 -c 'import json,sys; print(json.load(sys.stdin)["services"][sys.argv[1]]["image"])' "$1"
}

# state of a service: "running healthy", "running", "exited 0", "" (no container)
service_state() {
  dc ps -a --format json "$1" 2>/dev/null | python3 -c '
import json, sys
rows = [json.loads(l) for l in sys.stdin.read().splitlines() if l.strip()]
if rows and isinstance(rows[0], list): rows = rows[0]
for r in rows[:1]:
    print(r.get("State", ""), r.get("Health", "") or r.get("ExitCode", ""))'
}

require_db_running() {
  local state ids hint; state=$(service_state starlights-db)
  # deploy/'s own database is missing or stopped, but another Compose project runs one on this install's data folder:
  # starting deploy/'s would put a second SQL Server on the same files
  if [ "${#COMPOSE_ARGS[@]}" -eq 0 ] && [[ "$state" != running* ]]; then
    ids=$(data_mount_containers "$(project_name || true)")
    if [ -n "$ids" ]; then
      hint="Do not start deploy/'s own database next to it: two SQL Servers on one data folder damage it."
      [ -z "$state" ] || hint="$hint If that project is the one in use, remove deploy/'s stopped one: $(dc_shown) rm starlights-db"
      [ "$(basename "$0")" = backup.sh ] || hint="$hint
$(basename "$0") only works on an install run from deploy/ (backup.sh works with either)."
      # shellcheck disable=SC2086
      die "a database container that deploy/ did not start uses this install's data folder ($DATA_DIR/mssql): $(compose_projects_of $ids)." "$hint"
    fi
  fi
  case "$state" in
    "running healthy"*) ;;
    running*) die "the database container is running but not healthy yet ($state)." "Wait a minute and try again, or look at: $(dc_shown) logs starlights-db" ;;
    *) die "the database container is not running (state: ${state:-none})." "Start it with: $(dc_shown) up -d starlights-db   (then try again)" ;;
  esac
}

# runs SQL (from stdin) inside the database container; prints rows, fails on SQL errors
sql() {
  dc exec -T starlights-db bash -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" exec /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -b -h -1 -W -s "|" -i /dev/stdin'
}

db_exists() { [ "$(printf "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'%s';\nGO\n" "$DB_NAME" | sql | tr -d '[:space:]')" = "1" ]; }

# the migration ids the database has applied, one per line
db_migrations() {
  printf "SET NOCOUNT ON; IF OBJECT_ID(N'%s.dbo.__EFMigrationsHistory') IS NOT NULL SELECT MigrationId FROM %s.dbo.__EFMigrationsHistory ORDER BY MigrationId;\nGO\n" "$DB_NAME" "$DB_NAME" | sql | sed '/^\s*$/d'
}

# the migration ids a commit of the code knows (from the migration files' names), one per line
code_migrations() {
  { git -C "$REPO_DIR" ls-tree -r --name-only "${1:-HEAD}" -- src/modules \
    | grep -E '/Migrations/[0-9]{14}_[^/]+\.cs$' | grep -v '\.Designer\.cs$' || true; } \
    | sed -E 's#.*/##; s#\.cs$##' | sort -u
}

# counts that must never go down unnoticed, as "name|count" lines
DATA_COUNTS_SQL="SET NOCOUNT ON;
SELECT 'characters', COUNT(*) FROM $DB_NAME.characters.character;
SELECT 'campaigns', COUNT(*) FROM $DB_NAME.characters.campaign;
SELECT 'campaign_entries', COUNT(*) FROM $DB_NAME.characters.campaign_entry;
SELECT 'players', COUNT(*) FROM $DB_NAME.characters.player;
SELECT 'elements', COUNT(*) FROM $DB_NAME.elements.element;
GO"
db_counts() { printf '%s\n' "$DATA_COUNTS_SQL" | sql | sed '/^\s*$/d'; }

# per user folder: number of files and a fingerprint of their names, sizes and contents ("folder|files|sha256")
folder_fingerprints() {
  local root="${1:-$DATA_DIR}"
  docker run --rm --network none --security-opt no-new-privileges -v "$root:/d:ro" alpine:3.21 sh -c '
    for f in '"${USER_FOLDERS[*]}"'; do
      if [ -d "/d/$f" ]; then
        n=$(find "/d/$f" -type f | wc -l)
        h=$(cd "/d/$f" && find . -type f -exec sha256sum {} + | sort -k2 | sha256sum | cut -c1-64)
        echo "$f|$n|$h"
      else
        echo "$f|0|absent"
      fi
    done'
}

free_mb() { df -Pm "$1" | awk 'NR==2 {print $4}'; }

db_size_mb() {
  printf "SET NOCOUNT ON; SELECT CAST(SUM(size) * 8 / 1024 AS int) FROM %s.sys.database_files;\nGO\n" "$DB_NAME" | sql | tr -d '[:space:]'
}

sql_server_version() { printf "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64));\nGO\n" | sql | tr -d '[:space:]'; }

# what the running API says it is (commit), via the site's /api/admin/status with the admin key
api_commit() {
  local key; key=$(env_value STARLIGHTS_ADMIN_KEY || true)
  [ -n "$key" ] || return 0
  curl -fsS -m 10 -H @<(printf 'X-Admin-Key: %s\n' "$key") "http://127.0.0.1:$(site_port)/api/admin/status" 2>/dev/null \
    | python3 -c 'import json,sys; print(json.load(sys.stdin).get("commit",""))' 2>/dev/null || true
}

site_answers() { curl -fsS -m 10 -o /dev/null "http://127.0.0.1:$(site_port)/api/characters/creation-options" 2>/dev/null; }

wait_for_site() {
  local tries="${1:-60}"
  for _ in $(seq 1 "$tries"); do site_answers && return 0; sleep 3; done
  return 1
}

confirm_typed() {
  # $1 = what to type, $2 = what is about to happen, $3 = value of a --confirm flag (if given)
  local expected="$1" given="${3:-}"
  if [ -n "$given" ]; then
    [ "$given" = "$expected" ] || die "--confirm must be exactly '$expected' (got '$given'); nothing was changed."
    return 0
  fi
  [ -t 0 ] || die "this needs confirmation, but there is no terminal to ask on." "Run it in a terminal, or pass --confirm $expected"
  printf '\n%s\nType %s to go ahead (anything else stops): ' "$2" "$expected"
  local answer; read -r answer
  [ "$answer" = "$expected" ] || die "not confirmed; nothing was changed."
}

# the newest finished backup folder (names start with the date, so they sort by time)
newest_backup() {
  local d newest=""
  for d in "$BACKUP_ROOT"/*/; do
    [ -d "$d" ] || continue
    case "$d" in *.incomplete/) continue ;; esac
    newest="${d%/}"
  done
  printf '%s\n' "$newest"
}
