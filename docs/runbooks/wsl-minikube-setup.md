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
memory=12GB          # 12GB is REQUIRED for the full Phase 10c stack (10 GiB minikube VM + k6/IDE);
swap=4GB             # with memory=10GB, set MINIKUBE_MEMORY=8192 in .env — app stack only, no monitoring
processors=6
# do NOT set localhostForwarding=false — the whole Windows-access story (make tunnels) rides on it
```

Then `wsl --shutdown` and reopen Ubuntu.

Inside Ubuntu install the toolchain:

```bash
# .NET 10 SDK (Microsoft feed or `dotnet-install.sh`)
# Docker: either Docker Desktop with WSL integration enabled,
#         or docker-ce installed inside the distro (then `sudo service docker start`)
# kubectl + minikube + k6, and make
sudo apt-get update && sudo apt-get install -y make
# mkcert (cluster TLS, one static Go binary):
curl -sSL -o ~/.local/bin/mkcert \
  https://github.com/FiloSottile/mkcert/releases/download/v1.4.4/mkcert-v1.4.4-linux-amd64 \
  && chmod +x ~/.local/bin/mkcert
```

One-time kernel knob for the ingress tunnel (lets an unprivileged `kubectl port-forward`
bind local ports 80/443, which the Windows-browser flow needs — survives WSL restarts):

```bash
echo 'net.ipv4.ip_unprivileged_port_start=80' | sudo tee /etc/sysctl.d/99-forum-tunnels.conf
sudo sysctl -p /etc/sysctl.d/99-forum-tunnels.conf
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
make mk-up                        # calico CNI + ingress + metrics-server (Phase 10b)
make mk-tls                       # ONE-TIME: mkcert cert for forum.local (+minio/grafana SANs)
make mk-deploy ARGS=--seed        # images + all manifests + migration/seed Jobs, in order
make pods                         # watch it come up
make tunnels                      # admin tunnels (leave running; Ctrl+C stops all)

make mk-reset-db                  # wipe DB + re-migrate
make mk-down                      # delete namespace (ARGS=--stop or --delete for more)

make mon-up && make mon-check     # Phase 10c: monitoring stack (pinned Helm charts) + target check
make mon-down                     # reclaim the monitoring RAM when not benchmarking
```

`mk-deploy` builds both images straight into minikube's docker daemon
(`imagePullPolicy: Never`, no registry), generates every secret it needs from `.env`
(generate-if-missing: existing in-cluster secrets are never overwritten), applies
PSS-labelled namespace → RBAC → postgres/rabbitmq/minio → bucket Job → migration Job
→ (seed Job) → backend → frontend → ingress → NetworkPolicies, and waits at each gate.
Image tags are `git-<short-sha>[-dirty]`, substituted into the manifests at apply time —
`kubectl -n forum-dotnet rollout undo deployment/backend` maps to an exact build.

**NetworkPolicies are enforced** (calico; `APPLY_NETWORK_POLICIES=true` is the default
since Phase 10b). An old pre-calico cluster must be recreated once:
`make mk-down ARGS=--delete && make mk-up` — CNI cannot be swapped live.

## 4. Access from Windows (browser, DataGrip) — the two paths

The cluster and `kubectl` live inside the WSL2 VM; the browser and DataGrip run on
Windows. Two different network paths exist, and only one of them works from Windows:

**Fact 1 — WSL2 localhost forwarding.** A process listening on `localhost`/`127.0.0.1`
(or `0.0.0.0`) inside WSL2 is transparently reachable as `localhost:<port>` from
Windows, with zero configuration, unless `.wslconfig` sets `localhostForwarding=false`
(preflight checks this; this machine explicitly sets it `true`).
*Verified 2026-07-11 on this machine:* a server bound to `127.0.0.1:18099` in WSL
answered `curl.exe http://localhost:18099` from Windows.
⚠ Test with `curl.exe` or `Test-NetConnection`, **not** PowerShell's `Invoke-WebRequest`
— its .NET/IE proxy stack can abort localhost requests ("request was canceled") and
make you chase a network problem that doesn't exist.

**Fact 2 — the cluster IP is NOT reachable from Windows.** `minikube ip`
(e.g. `192.168.49.2`) is the docker-driver container's address on a docker bridge
*inside* the WSL VM. Windows reaches WSL's own interfaces via localhost forwarding,
but has no route into docker bridges behind them.
*Verified 2026-07-11 on this machine:* `Test-NetConnection 192.168.49.2 -Port 80`
from Windows → `TcpTestSucceeded=False` (ping also fails). Do not put `minikube ip`
into the **Windows** hosts file — it can never work. (From **inside WSL** that IP works
fine — that's the Section 3 flow.)

Conclusion: everything Windows-facing rides `kubectl port-forward` → WSL `127.0.0.1`
→ Windows `localhost`. That is not a workaround; port-forward tunnels through the
API server → kubelet, **not** through the CNI dataplane, so it is architecturally
unaffected by NetworkPolicies, Calico, or PSS. The hardening constrains pod-to-pod
traffic and what container processes may do — it cannot lock the admin out, and
turning policies off for "debugging access" is never necessary.

### 4a. Admin tools — `make tunnels`

`scripts/dev-tunnels.sh` opens every admin tunnel at once (auto-reconnects, one
Ctrl+C stops all) and prints the credentials from the cluster secrets. Local ports
are `remote + 10000` so they **never collide with docker-compose's published ports**
— with one compose stack and one cluster running simultaneously, each tool talks to
exactly the endpoint you asked for:

| Tool | Windows URL / connection | Notes |
|---|---|---|
| Backend API | `http://localhost:18080` | `/health/*`, `/metrics`, `/api/...`. **No Swagger UI**: it is mapped in Development only and the cluster runs Production — by design, not a bug. |
| Frontend (direct) | `http://localhost:13000` | bypasses ingress; API calls still go to `https://forum.local` (baked origin) — use 4b for the full app |
| RabbitMQ management | `http://localhost:25672` | creds printed by the script |
| MinIO console | `http://localhost:19001` | console port (9001); the S3 API (9000) is ingress-routed for presigned URLs and needs no tunnel |
| DataGrip / psql | `localhost:15432` | **15432, not 5432** — 5432 is the compose Postgres; the wrong port silently browses the wrong database |
| Ingress (full app) | `https://forum.local` (443/80) | see 4b; falls back to 8443 + a printed fix if the sysctl from §0 is missing |
| Grafana | `http://localhost:13001` (admin/admin) | Phase 10c; only opened when the `monitoring` namespace exists; the full-TLS path is `https://grafana.forum.local` via the ingress tunnel (4b) |
| Prometheus | `http://localhost:19090` | Phase 10c; `/targets`, `/rules`, `/alerts` — deliberately not ingress-exposed |

The Grafana/Prometheus entries target the REAL Service names of the Helm release
(`monitoring-grafana`, `monitoring-kube-prometheus-prometheus`) — verified against the
installed chart, since release name prefixes the chart's Service names.

### 4b. The real app under its real hostname — TLS/cookies/CORS/WS need the ingress path

Testing presigned uploads, the Secure refresh cookie, CORS and WebSocket timeouts
genuinely requires going through ingress-nginx with the real `Host`/SNI — a bare
port-forward to the backend Service can't exercise any of that. Three one-time steps:

1. **WSL kernel knob** (§0): lets the ingress tunnel bind real 443/80. `make tunnels`
   then forwards `443:443`/`80:80` from `svc/ingress-nginx-controller`.
2. **Windows hosts file** — `C:\Windows\System32\drivers\etc\hosts` in an
   **admin-elevated** editor (Windows blocks saving otherwise):
   ```
   127.0.0.1  forum.local minio.forum.local grafana.forum.local
   ```
   The browser still sends the real Host/SNI, so ingress host-routing and the cert
   SANs behave exactly as if you reached the cluster IP — only the hop changes. This
   also sidesteps `minikube tunnel` (sudo + foreground process, worse ergonomics).
3. **Trust the mkcert CA on Windows** — the cert was minted by the CA in the **WSL**
   store; the Windows browser consults the **Windows** store. Either:
   ```
   # WSL: copy the CA somewhere Windows sees
   cp "$(mkcert -CAROOT)/rootCA.pem" /mnt/c/Users/<you>/Downloads/mkcert-rootCA.pem
   # Windows terminal (a confirmation dialog pops up):
   certutil -user -addstore Root %USERPROFILE%\Downloads\mkcert-rootCA.pem
   ```
   or install mkcert.exe on Windows and run it against the shared CAROOT:
   `set CAROOT=\\wsl$\Ubuntu\home\<you>\.local\share\mkcert && mkcert -install`.
   Firefox keeps its own store: Settings → Certificates → Import, or
   `security.enterprise_roots.enabled=true`.

Then browse **`https://forum.local`** from Windows: padlock (issuer mkcert), login,
uploads (presigned PUT to `https://minio.forum.local` — same 127.0.0.1 route), LIVE
WebSocket pill. `grafana.forum.local` joins in Phase 10c with zero new mechanics.

Quick smoke test without the hosts file / trust steps (any terminal):

```
curl.exe -k https://forum.local/ --resolve forum.local:443:127.0.0.1
```

### 4c. WSL-internal flow (kept — fastest for curl iteration)

From inside WSL the cluster IP *is* routable; for quick checks skip the tunnels:

```bash
echo "$(minikube -p forum ip)  forum.local minio.forum.local" | sudo tee -a /etc/hosts   # once
curl --cacert "$(mkcert -CAROOT)/rootCA.pem" https://forum.local/api/content/categories
```

`/health/*` and `/metrics` are deliberately NOT ingress-routed (they're not under
`/api`) — use the backend tunnel (`localhost:18080/health/ready`) or `kubectl exec`-less
probes via port-forward.

### 4d. Security hardening vs. debuggability — what actually changes

- `kubectl port-forward`/`logs`/`describe`/`exec`: **unaffected** by netpols/calico/PSS
  (API-server path — see above). All admin flows in this runbook keep working with the
  full lockdown enforced.
- **Pod-to-pod** traffic: default-deny + explicit allows. A stray pod cannot reach
  postgres; the netpol probe in the deploy DoD proves it (expected FAIL).
- **PSS `restricted`** rejects non-compliant pods at admission — including one-off
  debug pods. A compliant probe pod looks like:
  ```bash
  kubectl run netpol-test -n forum-dotnet --rm -i --restart=Never --image=busybox:1.37 \
    --overrides='{"spec":{"securityContext":{"runAsNonRoot":true,"runAsUser":65534,"runAsGroup":65534,"seccompProfile":{"type":"RuntimeDefault"}},"containers":[{"name":"netpol-test","image":"busybox:1.37","command":["nc","-zv","-w","2","postgres","5432"],"securityContext":{"allowPrivilegeEscalation":false,"capabilities":{"drop":["ALL"]}}}]}}'
  ```
  (Expected: `timed out` — that's the policy working. Add `--labels=app=backend` and it
  connects.) The same applies to `kubectl debug` ephemeral containers on the shell-less
  chiseled backend: plain `--image=busybox` is rejected/unrunnable under `restricted`
  because busybox defaults to root — pass a compliant securityContext (e.g.
  `--profile=restricted` on kubectl ≥1.32 *plus* a non-root-compatible image) or debug
  via a separate probe pod like the one above.

## 5. Editing: keep the Windows GUI, run everything in Linux

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
`K8S_NAMESPACE`, `IMAGE_TAG` (default `git-<sha>`), `INGRESS_HOST`,
`RABBITMQ_USER/PASSWORD`, `JWT_SIGNING_KEY` (empty = generated),
`APPLY_NETWORK_POLICIES` (default true).
