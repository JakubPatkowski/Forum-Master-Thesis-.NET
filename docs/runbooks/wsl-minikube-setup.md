# Runbook: running forum-dotnet on WSL2 (Ubuntu) + minikube

This is the canonical local setup for Architecture A. The golden rule: **the repo
must live inside the Linux filesystem**, not on a `/mnt/<drive>` Windows mount.

## Why the Linux filesystem matters

WSL2 runs a real Linux kernel in a lightweight VM. When code sits on the Windows
drive (`/mnt/d/...`), every file read/write crosses the VM boundary over the 9P
protocol to NTFS — orders of magnitude slower for the many-small-file workloads
we hit constantly: `dotnet restore`/`build`/`test`, `git status`, Docker build
context, EF, file watching. Moving the repo into ext4 (`~/projects/...`) gives
near-native speed. This is the single biggest performance win.

Every script prints a warning if it detects it is running from `/mnt/`.

## 0. Prerequisites (once per machine)

On Windows, give WSL enough RAM for minikube. Create `%USERPROFILE%\.wslconfig`:

```ini
[wsl2]
memory=12GB
swap=4GB
processors=6
```

Then `wsl --shutdown` and reopen Ubuntu.

Inside Ubuntu install the toolchain:

```bash
# .NET 10 SDK (Microsoft feed or `dotnet-install.sh`)
# Docker: either Docker Desktop with WSL integration enabled,
#         or docker-ce installed inside the distro (then `sudo service docker start`)
# kubectl + minikube + k6, and make
sudo apt-get update && sudo apt-get install -y make
```

Verify everything at once:

```bash
make preflight        # or: bash scripts/preflight.sh
```

## 1. Get the code into WSL (from GitHub — do NOT copy from /mnt)

The repo already has a GitHub remote, so **push from Windows, clone in WSL** — that
keeps history, line endings (`.gitattributes` forces LF for `*.sh`) and permissions
clean. A raw file copy from `/mnt` drags along `bin/`, `obj/`, CRLF endings and the
`.git` internals byte-for-byte.

```bash
mkdir -p ~/projects && cd ~/projects
git clone https://github.com/JakubPatkowski/Forum-Master-Thesis-.NET.git forum-dotnet
cd forum-dotnet
chmod +x scripts/*.sh          # optional; the Makefile calls them via `bash` anyway
cp .env.example .env           # adjust if needed
```

> Commit and push any outstanding work on the Windows side first, otherwise it
> won't be in the clone.

## 2. Local development loop (fast inner loop)

Run backing services in containers, run the API from `dotnet` (or your IDE):

```bash
make infra-up          # Postgres + RabbitMQ + MinIO (docker compose)
make api ARGS=--migrate    # migrate + views, then run Forum.Api on :8080
# ... http://localhost:8080/health/live
make infra-down            # stop (add ARGS=--volumes to wipe data)
```

Unit + architecture + integration tests (Testcontainers needs Docker running):

```bash
make test
```

## 3. Cluster run (minikube — the "real environment" simulation)

```bash
make mk-up                 # start the cluster (docker driver, ingress addon)
# one-time host mapping printed by mk-up:
#   echo "$(minikube -p forum ip)  forum.local" | sudo tee -a /etc/hosts
make mk-deploy             # build image into minikube + apply manifests in order
make pods                  # watch it come up
make urls                  # http://forum.local/api

make mk-reset-db           # wipe DB + re-migrate
make mk-down               # delete namespace (ARGS=--stop or --delete for more)
```

`mk-deploy` builds straight into minikube's docker daemon (`imagePullPolicy: Never`,
no registry needed), generates the `postgres-credentials` secret the manifests
expect (`POSTGRES_*` + `CONNECTION_STRING`), runs the migration **Job** and waits
for it before rolling out the backend.

NetworkPolicies stay off by default: `default-deny-ingress` has no companion
allow-rules yet, so enabling it would block ingress→backend and backend→postgres.
Flip `APPLY_NETWORK_POLICIES=true` in `.env` only once allow-rules exist.

## 4. Editing: keep the Windows GUI, run everything in Linux

You do **not** lose a graphical editor by moving to WSL:

- **VS Code + "WSL" extension** — the UI runs on Windows, the language server,
  terminal, git and Claude Code all run inside the distro against the ext4 files.
  `cd ~/projects/forum-dotnet && code .`
- **JetBrains Rider / Gateway** — open the project from the distro; use a WSL
  .NET SDK.

Run **Claude Code inside WSL** (`claude` in the Ubuntu terminal, ideally VS Code's
integrated one). Pointing Windows-side Claude Code at `\\wsl$\Ubuntu\...` works but
is slow and permission-fragile — avoid it.

## Config knobs

All in `.env` (see `.env.example`): DB creds, `MINIKUBE_CPUS/MEMORY/DRIVER`,
`K8S_NAMESPACE`, `IMAGE_TAG`, `INGRESS_HOST`, `APPLY_NETWORK_POLICIES`.
