/**
 * Playwright end-to-end suite — the production SPA bundle rendered by a real browser against the
 * REAL .NET API on the deterministic Development seed. Nothing is mocked: this is the layer that
 * catches what the 248 backend tests and the Vitest suite cannot see, e.g. the anonymous home page
 * breaking when the API started answering 404 for private categories (e2e/home-anonymous.spec.ts).
 *
 * There is deliberately NO `webServer` block — the stack is external, started once and shared:
 *
 *   make infra-up                        # Postgres :5432 + RabbitMQ + MinIO (docker compose, project forum-dotnet)
 *   RateLimiting__Global__PermitLimit=100000 RateLimiting__Auth__PermitLimit=1000 \
 *     make api ARGS=--migrate            # migrates, then serves http://localhost:8080 (blocks; background it)
 *   make seed                            # Development profile → forum_net (non-empty DB: make seed ARGS=--force)
 *   cd frontend && NEXT_PUBLIC_API_URL=http://localhost:8080 npm run build && npx next start -p 3000
 *   npm run test:e2e                     # both projects · npm run test:e2e:android for the phone only
 *
 * The suite drives the PRODUCTION bundle (`next build` + `next start`, as scripts/lighthouse.sh does),
 * never `next dev`: dev mode adds an overlay, lazy compilation and HMR sockets that would make the
 * mobile/overflow and timing assertions meaningless. The rate-limiter overrides above are the backend's
 * documented test/benchmark knobs (defaults 100 req/min per IP, 10/min on /api/identity/*): one browser
 * page load fans out into ~15 API calls (feed + reactions batch + per-card icon lookups), so a full run
 * crosses the default budget and a 429 would masquerade as a product failure.
 *
 * Environment:
 *   E2E_BASE_URL     SPA origin to visit            (default http://localhost:3000; e.g. https://forum.local)
 *   E2E_API_URL      API origin for test preconditions (default http://localhost:8080; must be the API the
 *                    SPA bundle was built against — NEXT_PUBLIC_API_URL)
 *   E2E_CHROME_PATH  browser executable. Default: the portable chrome-headless-shell wrapper at
 *                    ../.bench/chrome/chrome-wrapper when it exists (`scripts/lighthouse.sh --bootstrap-chrome`,
 *                    no sudo, no `playwright install`); otherwise Playwright's own browser, if installed.
 *
 * Seed state is shared by every test (one worker, no parallelism): tests only ADD rows with unique
 * titles and never delete, so a run is repeatable; `make seed ARGS=--force` resets the dataset.
 */

import { existsSync } from "node:fs";
import path from "node:path";

import { defineConfig, devices } from "@playwright/test";

const PORTABLE_CHROME = path.resolve(__dirname, "..", ".bench", "chrome", "chrome-wrapper");

function resolveExecutablePath(): string | undefined {
  if (process.env.E2E_CHROME_PATH) return process.env.E2E_CHROME_PATH;
  return existsSync(PORTABLE_CHROME) ? PORTABLE_CHROME : undefined;
}

const baseURL = process.env.E2E_BASE_URL ?? "http://localhost:3000";
// Worker processes inherit this environment: resolve the default once so e2e/helpers.ts and the
// config can never disagree about which API the preconditions talk to.
process.env.E2E_API_URL = process.env.E2E_API_URL ?? "http://localhost:8080";

export default defineConfig({
  testDir: "./e2e",
  outputDir: "test-results",
  // One worker, no in-file parallelism: every test shares the live seed database.
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env.CI,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  reporter: [["list"], ["html", { outputFolder: "playwright-report", open: "never" }]],
  use: {
    baseURL,
    // The same suite must be able to point at the cluster's mkcert-signed https://forum.local.
    ignoreHTTPSErrors: true,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    launchOptions: {
      executablePath: resolveExecutablePath(),
      // --no-sandbox: unprivileged WSL/CI user namespaces; --disable-dev-shm-usage: tiny /dev/shm in containers.
      args: ["--no-sandbox", "--disable-dev-shm-usage"],
    },
  },
  projects: [
    { name: "desktop", use: { ...devices["Desktop Chrome"] } },
    // Chrome-for-Android UA, touch events, 412×915 CSS px at DPR 2.625 — the checks Hubert asked for.
    { name: "android", use: { ...devices["Pixel 7"] } },
  ],
});
