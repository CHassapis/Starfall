# Install Starfall with Docker

Starfall runs in your browser. Install it once on a computer that stays on while your group plays.
The same `deploy/docker-compose.yml` runs on Linux and on Windows through WSL2. No desktop application is needed.

**Validation status:** on Ubuntu 24.04 with real Docker (disposable installs): a clean install, a repeated setup,
recovery with a stopped API, and backup, deep verification, restore, update and rollback with the maintenance tools,
including a `.env` saved with Windows (CRLF) line endings. On 2026-10-20 also: a fresh `setup.sh` **with** the
content download (Aurora Legacy and 5etools from GitHub, Compendium built) followed by importing 14 real Aurora
characters, and an install of v0.10.0 with real content and characters updated with `maintenance/update.sh`, then
`verify.sh --deep`, `rollback.sh`, `restore.sh` (data identical to before the update) and a second update. Those two
ran from a clone with one test-only change binding the site to 127.0.0.1. The installer's 39 contract tests pass.
**Not yet tested:** Windows (Docker Desktop, WSL2, the PowerShell launcher) on real Windows hardware. The Windows
instructions and launcher are implemented but unverified on Windows.

## Before you start

- An **Intel/AMD x86-64** computer. ARM machines, including Raspberry Pi and Windows on ARM, are unsupported.
- Linux: Ubuntu or Debian, Docker Engine and the Docker Compose plugin.
- Windows: Windows 11, or a Windows 10 system compatible with your Docker Desktop release, with **WSL2 and Linux containers**.
  Check [Docker's current Windows requirements](https://docs.docker.com/desktop/setup/install/windows-install/), including
  OS servicing eligibility, virtualization and WSL version. Do not assume every Windows 10 installation is supported.
- At least **4 GB RAM available to Docker**; 8 GB or more is recommended. Windows also needs memory for Windows itself;
  follow Docker Desktop's host requirements. The installer warns when Linux has less than 2 GB currently available.
- At least **10 GB free** on the installation filesystem and Docker image storage; **20 GB or more** is recommended for
  builds, downloaded content and growth. Docker Desktop storage is inside its VM and may need a separate space check.
- Internet access for source, container images, package builds and content downloads. The first setup may take 20–40 minutes.
- `git` and Python 3 in Linux/WSL, and `curl` for the maintenance tools (backups, updates). `openssl` is not needed
  by the new setup helper. Run setup as your normal user, with permission to use Docker.

Setup checks prerequisites, creates private credentials once, prepares data permissions, builds the images, starts the
app and imports content. It installs no system packages and changes no firewall settings. It creates no administrator
account on your computer. Docker itself and any system prerequisites must be installed separately by you or your administrator.

## Linux: Ubuntu or Debian

1. Install Docker Engine and its Compose plugin using Docker's instructions for
   [Ubuntu](https://docs.docker.com/engine/install/ubuntu/) or [Debian](https://docs.docker.com/engine/install/debian/).
   Follow the [Linux post-installation instructions](https://docs.docker.com/engine/install/linux-postinstall/) if your user
   cannot access Docker. Membership in the Docker group grants administrative power; follow your server administrator's policy.
   Install `git` and `python3` with your distribution's package manager if missing.
2. In a terminal, verify that these commands work **without sudo**:

   ```bash
   docker info
   docker compose version
   python3 --version
   git --version
   ```

3. Clone into a new directory you own. Never use an existing live deployment directory for testing:

   ```bash
   cd ~
   git clone --branch aurora-server https://github.com/CHassapis/starfall.git
   cd starfall/deploy
   ./setup.sh --check
   ./setup.sh
   ```

`--check` does not create credentials, containers or data. It cannot prove that image downloads, builds or container
write permissions will succeed; those are tested during setup. If the default port is busy on a new installation, use
`./setup.sh --port 8094` instead. After `.env` exists, the chosen port is saved there.

## Windows: Docker Desktop and WSL2

Keep the checkout and database in the **WSL Linux filesystem**, for example `/home/alice/starfall`.
Do not clone into `C:\`, OneDrive, a network share or `/mnt/c`. Linux database ownership and bind mounts depend on Linux
filesystem behavior. Docker also recommends [keeping bind-mounted files in Linux](https://docs.docker.com/desktop/features/wsl/best-practices/).

1. Follow Microsoft's [WSL installation instructions](https://learn.microsoft.com/en-us/windows/wsl/install) to install
   WSL2 and Ubuntu. Windows may ask for administrator approval and a reboot during this separate prerequisite step.
   Open Ubuntu once and create your Linux username and password.
2. Install and start [Docker Desktop for Windows](https://docs.docker.com/desktop/setup/install/windows-install/).
   Select the **WSL2 backend** and **Linux containers**. Under **Settings → Resources → WSL Integration**, enable Ubuntu
   (or the distribution you chose). See [Docker's WSL integration guide](https://docs.docker.com/desktop/features/wsl/).
   Use Docker Desktop's engine; do not install a second Docker Engine inside that distribution for this installation.
3. In PowerShell, inspect your distribution names and versions:

   ```powershell
   wsl --version
   wsl --list --verbose
   ```

   The Ubuntu row must show **VERSION 2**. If it shows 1, follow Microsoft's WSL conversion instructions before continuing.
   `Ubuntu-24.04` and `Ubuntu` are different distribution names; use the name shown on your machine.
4. Open **Ubuntu** from the Start menu. Verify `git`, `python3`, `docker info` and `docker compose version` work there.
   If Git or Python is missing, install it through Ubuntu's package manager with your administrator's approval.
   Clone and run setup in the Ubuntu terminal:

   ```bash
   cd ~
   git clone --branch aurora-server https://github.com/CHassapis/starfall.git
   cd starfall/deploy
   ./setup.sh --check
   ./setup.sh
   ```

5. Open **http://localhost:8093** in your normal Windows browser (or the port you selected).
   Leave Docker Desktop running while you use Starfall. Linux restart policies do not start Docker Desktop itself after
   Windows signs out or reboots; check Docker Desktop's startup setting.

### Optional PowerShell launcher

After cloning inside Ubuntu, you can run setup from PowerShell using the included `setup.ps1`:

```powershell
& '\\wsl.localhost\Ubuntu\home\alice\starfall\deploy\setup.ps1' -Distribution Ubuntu -LinuxPath '/home/alice/starfall/deploy' -Check
& '\\wsl.localhost\Ubuntu\home\alice\starfall\deploy\setup.ps1' -Distribution Ubuntu -LinuxPath '/home/alice/starfall/deploy'
```

Replace `Ubuntu` and `alice` with your distribution name and Linux username. You can also invoke a copy of `setup.ps1`
from a Windows folder; `-LinuxPath` must still identify the Linux checkout's `deploy` directory. Quote paths containing
spaces, for example `-LinuxPath '/home/alice/My Games/starlights/deploy'`. The launcher accepts `-Port 8094` for a new
installation and `-SkipContent` to defer content. It passes the path as data, not executable shell text.

If PowerShell blocks script execution, use the Ubuntu terminal method above; changing a system-wide execution policy
is unnecessary. The launcher runs the Python helper directly, so CRLF line endings in a Windows copy of `setup.ps1`
do not affect Bash. Git attributes keep the Linux entrypoint LF. The `.env` reader accepts LF and CRLF.

## Open Starfall and start playing

- On the Linux server itself, or the Windows computer running Docker Desktop: **http://localhost:8093**.
- From another computer or phone on the same home network: **http://192.168.1.20:8093**, replacing `192.168.1.20` with your
  server's LAN address. On Linux, `hostname -I` lists addresses; your router also lists connected devices. On Windows,
  `ipconfig` shows the host's LAN IPv4 address. Do not use the WSL VM address from another device.
- If you selected a different port, replace `8093` in the address.
- For LAN access, your host firewall must allow the chosen TCP port on your private network. The installer does not
  change Windows Firewall or Linux firewall rules. Docker Desktop LAN access depends on your host/network settings;
  try localhost first. Do not set up router port forwarding to the public internet.
- To install Starfall on phones (home-screen app) or to play away from home you need an **https** address: see the
  README's [HTTPS](../README.md#https) section (Tailscale Serve keeps it private).

Select your player name, then create a character after content import finishes. Use **Save** to keep character changes.
For administrator access, open `deploy/.env` privately in an editor and read `STARLIGHTS_MASTER_PASSWORD`. Enter it using
the **key button** in Starfall' header. This password gives broad access to characters and campaigns; keep it private.
Setup never prints it. Do not paste `.env`, rendered Compose configuration or container inspection output into support requests.

## Repeat setup or recover from an interruption

Rerun `./setup.sh` from the **same `deploy` directory**, or run the same PowerShell launcher command. Keep `.env` and
`data/` together. The installer does not delete data, reset databases, replace existing credentials or force a content rebuild.
Existing containers are kept with `--no-recreate`. When all five service containers exist, their images are reused without
building again. If a container is missing, setup builds from this checkout; the remaining containers keep their images.
Use the maintenance tools for upgrades, not a mixture of old containers and a newer checkout.

A completed content import is detected from the server's persisted success status. Setup then leaves content and characters
unchanged. If an import is running, setup waits for it. If an initial import failed or was interrupted, setup retries through
the existing non-forced content update API. That API may update imported rules and reprocess characters; it is not a database
rollback. Do not use a production installation as a recovery test.

If you press Ctrl+C or a timeout expires, keep all generated files and run setup again. Containers already started and
server-side content jobs may continue running. Setup never stops them as an automatic cleanup action. Normal database
migrations run as defined by Compose; setup does not undo migrations if a later step fails.

If `.env` is missing but data exists, setup refuses to generate new passwords. Restore the **matching original `.env`**
from a backup. Never change `STARLIGHTS_SA_PASSWORD` for an existing database merely to make setup pass.
Do not change `COMPOSE_PROJECT_NAME` after installation: it identifies the containers using this data directory.
New installations save a unique project name in `.env`; older installations retain Compose's existing directory-based name.
`data/.installer-project` prevents accidental reassignment after this installer has run; preserve it with your data.

`./setup.sh --skip-content` starts the app without downloading content. Import it later through the **Content** page or rerun
setup. Some character choices will be missing until import succeeds, and saving homebrew fails (with an error, after
the homebrew file itself is saved) until the content has been imported once.

## Timeouts and troubleshooting

Default limits: image build **30 minutes**, startup and migrations **5 minutes**, web/API readiness **3 minutes**, content
import wait **30 minutes**. These are separate stages, not one overall deadline. Local HTTP calls also have short timeouts.
On a slow machine, increase a specific limit from the Linux/Ubuntu terminal:

```bash
STARLIGHTS_SETUP_BUILD_TIMEOUT=3600 STARLIGHTS_SETUP_CONTENT_TIMEOUT=3600 ./setup.sh
```

The other variables are `STARLIGHTS_SETUP_START_TIMEOUT` and `STARLIGHTS_SETUP_HEALTH_TIMEOUT`. Values are seconds,
from 1 to 86400. Timeout overrides are supplied in the Linux/WSL shell; the PowerShell launcher does not forward Windows
environment variables. An exit code of **0** means setup completed, **1** means installation failed, **2** means the app
started but content is incomplete, and **130** means the user interrupted setup.

| Problem | What to do |
| --- | --- |
| Docker is missing, stopped or permission denied | Make `docker info` work as your normal user. On Windows, start Docker Desktop and enable your distribution's WSL integration. Do not run setup with sudo. |
| WSL1 or Windows containers detected | Use WSL2 and switch Docker Desktop to Linux containers. |
| Windows/UNC path rejected | Clone in Ubuntu's home directory. PowerShell's `-LinuxPath` uses `/home/...`, not a drive letter or UNC path. |
| Port is unavailable | For a new installation, try `./setup.sh --port 8094`. For an existing one, inspect `STARLIGHTS_PORT` in `.env`; changing a running container's port requires the maintenance/reconfiguration workflow, since setup does not recreate it. |
| `.env` permissions or format rejected | The file must belong to your Linux user and have mode 600: `chmod 600 .env`. Use literal `KEY=value` assignments without quotes, spaces, duplicate keys or substitutions. Preserve existing secret values; unsupported custom credentials need manual review, not automatic replacement. |
| Container directory permission check fails | Setup only assigns owners to empty directories. Existing data is never recursively re-owned. Restore correct ownership from your backup/maintenance procedure; do not use `chmod -R 777`. API directories use UID 1654; SQL uses UID 10001. |
| Low RAM or disk | Free space or increase the Docker/WSL allocation. Check both the checkout's disk and Docker's image storage. Setup never prunes images or data. |
| Build fails or times out | Check internet access, free memory/disk and the time limit. A repeat uses Docker's build cache. For local diagnosis only, run `docker compose build` in this install's `deploy` directory; review output for secrets before sharing. |
| Startup or health timeout | Run `docker compose ps -a` in this install's `deploy` directory. Inspect migration workers as well as the database/API. Both migration workers must exit successfully. Fix the cause, then rerun setup. |
| Content download/import fails | The app remains running. Unlock admin access, open **Content**, and inspect source links/progress/errors. Check GitHub connectivity or rate limits and disk space. Rerun setup to wait for or retry the initial import. |
| Another setup is running | Let it finish or interrupt that terminal. The directory lock is released automatically when setup exits. |
| Project belongs to a different directory | Stop and verify you are using the intended checkout. For a new, separate installation, use its own data and unique project name. Never reuse a live project's name or data. |

For deeper diagnosis, `docker compose logs --tail=100 starlights-db starlights-migrate-elements starlights-migrate-characters starlights-api`
shows service logs. Read them locally: application logs can contain private content or configuration. The installer deliberately
withholds raw build/Compose/HTTP output and does not save diagnostic logs. Do not share secrets in bug reports.

## Validation and maintenance handoff

Run `bash -n deploy/setup.sh` and `python3 -m unittest discover -s deploy/installer-tests -v` from the repository root.
The tests use temporary directories (including spaces in their paths), synthetic credentials/data and simulated Docker/API
responses. They cover repeat setup, build/start/health/content timeouts, content retry/attachment, secret handling, port
conflicts, permissions, WSL filesystem rejection and prerequisite failures. They do **not** prove SQL persistence or a real
Windows Docker Desktop installation.

A real disposable test runner is provided at `deploy/installer-tests/docker_smoke.py`. It copies tracked source into a new
temporary checkout, uses unique test image/project names and a loopback-only high port, and checks a synthetic SQL row
survives a repeated setup. It never uses a live data directory. See its `--help` before running it on a Docker-capable host.
A complete content download and browser character-creation check still need to be performed in that disposable installation.

### After installing: backups, restores and updates

Backups, restores and updates are done with `deploy/maintenance/` (`backup.sh`, `verify.sh`, `restore.sh`,
`update.sh`, `rollback.sh`); see [maintenance.md](maintenance.md). They work on the install setup made: they read the
same `.env` (including `COMPOSE_PROJECT_NAME`, LF or CRLF line endings), keep their backups in `data/backups/`
(which setup never touches), and leave `data/.installer-project` alone. `admin.sh` also accepts a CRLF `.env`.
Docker images are named after `COMPOSE_PROJECT_NAME`, so separate installations on one computer do not share images.
`.dockerignore` keeps `deploy/.env` and all of `deploy/data/` (including backups) out of Docker build contexts.
