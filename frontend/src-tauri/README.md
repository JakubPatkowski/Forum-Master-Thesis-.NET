# Forum Desktop — Tauri v2 shell (Architecture A)

A native desktop window around the deployed forum SPA (plan item A-1). **Hosted-URL mode**:
the webview navigates to a running Next.js frontend exactly like a browser tab — nothing of
the app runs inside this process (A's stack — .NET API + Postgres + RabbitMQ + MinIO — cannot
be bundled into an installer the way B bundles its single Go binary; this is the same shape as
B's *mobile* plan in `../../../gomx/MOBILE.md`, not B's sidecar desktop app). Static export is
not an option either: `frontend/next.config.ts` rules it out (runtime-ULID routes).

## Window & security model

| Window     | Content                              | Tauri IPC / capabilities                              |
| ---------- | ------------------------------------ | ----------------------------------------------------- |
| `main`     | the remote SPA (configured server)   | **none** — remote origins get no IPC in Tauri v2, and no capability lists this window |
| `settings` | local bundled page (`ui/`)           | `core:default` + the app's 3 server-URL commands (`capabilities/settings.json`) |

- **CSP** (`tauri.conf.json > app.security.csp`) applies to the bundled `ui/` page: no remote
  scripts/styles/images, `connect-src` limited to the Tauri IPC endpoints. The remote SPA is
  governed by its own server's headers, same as in a browser — the shell cannot (and should
  not) inject CSP into remote origins.
- **Navigation policy** (`src/lib.rs`): the main window may only navigate within the
  configured server's origin. Any other http(s) URL (markdown external links, presigned MinIO
  attachment links — both `target="_blank"`, rewritten by an init script into same-window
  navigations) opens in the **system browser**; non-http schemes are dropped.
- No shell/spawn permissions, no sidecar, no fs/store plugins — settings persist via plain
  Rust `std::fs` into the app config dir.

## Server URL: build-time default + runtime override

Follows the frontend Docker image precedent (`NEXT_PUBLIC_API_URL` baked at build time):

- **Default** — compiled in: `http://localhost:3000`, overridable per build:

  ```bash
  FORUM_DESKTOP_SERVER_URL=https://forum.local npm run tauri:build
  ```

  (On Windows PowerShell: `$env:FORUM_DESKTOP_SERVER_URL="https://forum.local"; npm run tauri:build`.)

- **Runtime override** — menu **Forum → Server Settings…** (Ctrl+,). Validated (http/https
  origin only, no path — a `/api` suffix is the classic footgun, see `frontend/Dockerfile`),
  persisted across restarts at:
  - Windows: `%APPDATA%\com.forumdotnet.desktop\settings.json`
  - Linux: `~/.config/com.forumdotnet.desktop/settings.json`

  The URL is the **frontend origin**; the SPA served from it brings its own baked API/WS
  endpoints, so one knob covers everything.

Why no backend CORS or cookie changes: in hosted-URL mode the webview's document origin is
the URL it navigated to (never `tauri://`). Dev (`http://localhost:3000` page →
`localhost:5099` API) is cross-origin but **same-site**, so the `SameSite=Strict` refresh
cookie flows and the existing CORS allow-list already contains the origin; against the cluster
(`https://forum.local`) frontend and API are same-origin behind the ingress and CORS is not
involved at all.

## Prerequisites

**Windows (primary target)**

1. [Rust](https://www.rust-lang.org/tools/install) (rustup, default MSVC host).
2. Visual Studio Build Tools with the **Desktop development with C++** workload.
3. Node.js ≥ 20.
4. WebView2 Runtime — preinstalled on Windows 11; the produced installer uses
   `webviewInstallMode: downloadBootstrapper`, so on Windows 10 it downloads/installs the
   runtime itself.

**Ubuntu / WSLg** — Rust plus the official Tauri v2 Linux stack:

```bash
sudo apt install libwebkit2gtk-4.1-dev build-essential curl wget file \
  libxdo-dev libssl-dev libayatana-appindicator3-dev librsvg2-dev
```

## Development

```bash
# Terminal 1 (WSL) — backend API on :5099 (infra via compose, then):
cd backend && dotnet run --project src/Bootstrap/Forum.Api

# Terminal 2 (WSL) — the SPA dev server on :3000:
cd frontend && npm run dev

# Terminal 3 — the native window (loads http://localhost:3000):
cd frontend && npm run tauri
```

Run terminal 3 **on Windows** for the real WebView2 experience: WSL's localhost forwarding
(`.wslconfig` `localhostForwarding=true`, already required by the existing runbooks) makes the
WSL-hosted `localhost:3000`/`5099` reachable from Windows, so the servers stay in WSL and only
the shell runs natively. Building Rust against a `\\wsl$` checkout is slow — prefer a
Windows-side clone of the repo for the shell dev loop.

To point the shell at the k8s cluster instead, set the server URL to `https://forum.local`
(settings window or build-time env). Prereqs are the same as for browser access —
`docs/runbooks/wsl-minikube-setup.md` §4: hosts-file entries, `make tunnels`, and the mkcert
CA trusted by the OS (WebView2 reads the **Windows** certificate store — §4b; WebKitGTK reads
the system store that `mkcert -install` populates).

## Production build

```bash
cd frontend && npm run tauri:build           # add FORUM_DESKTOP_SERVER_URL=… to re-target
```

- **Windows**: NSIS installer (+ MSI) under `src-tauri/target/release/bundle/`. Must be built
  on Windows (the bundler drives WiX/NSIS and links against the MSVC toolchain).
- **Linux**: `.deb`/`.rpm`/AppImage. WSLg caveats: AppImage packaging may need
  `NO_STRIP=true` and FUSE (`libfuse2`) for linuxdeploy; the `.deb` target is the
  low-friction one. The window itself runs fine under WSLg (Wayland).

Icons are generated placeholders (Decision 3): swap `app-icon.png` and re-run
`npx tauri icon src-tauri/app-icon.png` from `frontend/` (delete the `icons/android`/`ios`
output folders it also emits — mobile is out of scope).

## Manual verification checklist (parity with the browser)

- [ ] Login → refresh cookie set (DevTools: right-click window → Inspect works in dev builds);
      silent refresh after 15 min keeps the session; logout clears it.
- [ ] Browse feed / thread / category / search / social pages.
- [ ] LIVE pill turns green; two sessions (Tauri window + a browser tab): a reaction/comment
      in one appears in the other without refresh (WS push).
- [ ] Image upload in the composer (presigned MinIO PUT) and the image renders in preview.
- [ ] Attachment link and an external markdown link open in the **system browser**.
- [ ] OS sleep/resume or long laptop-lid close: the socket reconnects (RECONNECTING → LIVE)
      and the feed resyncs.
- [ ] Server Settings: invalid URL rejected inline; valid URL persists across app restart;
      Reset returns to the baked default.

## Mobile (out of scope here)

Deliberately not scaffolded in this phase. The path is the same as B's chosen mobile plan
(`gomx/MOBILE.md` Option 1 — hosted shell, no sidecar): this shell is *already* that shape, so
a future session only adds `tauri.android.conf.json`/`tauri.ios.conf.json` (pointing at an
HTTPS deployment), the mobile Rust targets, and the per-platform toolchains (Android
SDK/NDK; Xcode on macOS). See plan item A-1's mobile note in
`docs/architecture/AB-UNIFICATION-PLAN-ARCHITECTURE-A.md`.
