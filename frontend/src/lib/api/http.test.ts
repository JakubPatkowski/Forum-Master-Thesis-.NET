import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { apiFetch } from "@/lib/api/http";
import { ApiError } from "@/lib/api/problem";
import { clearAccessToken, getAccessToken, setAccessToken } from "@/lib/auth/token-store";

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const FUTURE = new Date(Date.now() + 15 * 60_000).toISOString();

describe("apiFetch 401 → silent refresh → retry-once interceptor (brief §4.2)", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    vi.stubGlobal("fetch", fetchMock);
    fetchMock.mockReset();
    setAccessToken("old-token", FUTURE);
  });

  afterEach(() => {
    clearAccessToken();
    vi.unstubAllGlobals();
  });

  it("refreshes once and retries the original call with the new token", async () => {
    fetchMock.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url.endsWith("/api/identity/refresh")) {
        return jsonResponse(200, { accessToken: "new-token", expiresOnUtc: FUTURE });
      }
      const auth = new Headers(init?.headers).get("Authorization");
      return auth === "Bearer new-token" ? jsonResponse(200, { ok: true }) : jsonResponse(401, {});
    });

    const result = await apiFetch<{ ok: boolean }>("/api/x");
    expect(result.ok).toBe(true);
    expect(getAccessToken()).toBe("new-token");
    const refreshCalls = fetchMock.mock.calls.filter(([url]) =>
      String(url).endsWith("/api/identity/refresh"),
    );
    expect(refreshCalls).toHaveLength(1);
  });

  it("shares one refresh across concurrent 401s (single-flight)", async () => {
    fetchMock.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url.endsWith("/api/identity/refresh")) {
        await new Promise((resolve) => setTimeout(resolve, 20));
        return jsonResponse(200, { accessToken: "new-token", expiresOnUtc: FUTURE });
      }
      const auth = new Headers(init?.headers).get("Authorization");
      return auth === "Bearer new-token" ? jsonResponse(200, { ok: true }) : jsonResponse(401, {});
    });

    const [a, b, c] = await Promise.all([
      apiFetch<{ ok: boolean }>("/api/a"),
      apiFetch<{ ok: boolean }>("/api/b"),
      apiFetch<{ ok: boolean }>("/api/c"),
    ]);
    expect(a.ok && b.ok && c.ok).toBe(true);
    const refreshCalls = fetchMock.mock.calls.filter(([url]) =>
      String(url).endsWith("/api/identity/refresh"),
    );
    expect(refreshCalls).toHaveLength(1);
  });

  it("hard-logs-out (clears the token) when the refresh itself fails", async () => {
    fetchMock.mockImplementation(async (url: string) => {
      if (url.endsWith("/api/identity/refresh")) return jsonResponse(401, {});
      return jsonResponse(401, {});
    });

    await expect(apiFetch("/api/x")).rejects.toBeInstanceOf(ApiError);
    expect(getAccessToken()).toBeNull();
  });

  it("retries at most once — a second 401 propagates instead of looping", async () => {
    fetchMock.mockImplementation(async (url: string) => {
      if (url.endsWith("/api/identity/refresh")) {
        return jsonResponse(200, { accessToken: "new-token", expiresOnUtc: FUTURE });
      }
      return jsonResponse(401, {});
    });

    await expect(apiFetch("/api/x")).rejects.toMatchObject({ status: 401 });
    // one original + one refresh + one retry = 3
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it("does not attempt refresh for anonymous (auth:false) calls", async () => {
    fetchMock.mockImplementation(async () => jsonResponse(401, {}));
    await expect(
      apiFetch("/api/identity/login", { method: "POST", body: {}, auth: false }),
    ).rejects.toMatchObject({ status: 401 });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
