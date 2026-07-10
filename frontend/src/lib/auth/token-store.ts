/**
 * In-memory access-token store + single-flight silent refresh.
 *
 * The access token lives ONLY here (a module-scope variable) — never in localStorage,
 * sessionStorage or a JS-readable cookie. The refresh token is an httpOnly cookie
 * path-scoped to /api/identity that the browser attaches by itself; JS never sees it.
 *
 * Standalone (not a React context) so both the HTTP client's 401-retry interceptor and
 * the AuthProvider share the exact same single-flight refresh, with React subscribing
 * via `subscribe` (used through useSyncExternalStore).
 */

import { apiUrl } from "@/lib/config";

export interface AccessTokenState {
  token: string | null;
  /** Epoch millis the token expires at (from the login/refresh response). */
  expiresAt: number | null;
}

type Listener = () => void;

let state: AccessTokenState = { token: null, expiresAt: null };
let refreshInFlight: Promise<boolean> | null = null;
const listeners = new Set<Listener>();

function notify(): void {
  for (const listener of listeners) listener();
}

export function getAccessToken(): string | null {
  return state.token;
}

export function getTokenState(): AccessTokenState {
  return state;
}

export function setAccessToken(token: string, expiresOnUtc: string): void {
  state = { token, expiresAt: Date.parse(expiresOnUtc) };
  notify();
}

export function clearAccessToken(): void {
  if (state.token === null) return;
  state = { token: null, expiresAt: null };
  notify();
}

export function subscribe(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/**
 * Silent refresh — single-flight: concurrent 401s share one POST /refresh. Any failure
 * means hard logout; the response gives no way to distinguish expired vs reused vs
 * garbage refresh tokens, so no cause-specific handling exists on purpose.
 */
export function refreshAccessToken(): Promise<boolean> {
  if (refreshInFlight) return refreshInFlight;

  refreshInFlight = (async () => {
    try {
      const response = await fetch(`${apiUrl}/api/identity/refresh`, {
        method: "POST",
        credentials: "include",
      });
      if (!response.ok) {
        clearAccessToken();
        return false;
      }
      const body = (await response.json()) as { accessToken: string; expiresOnUtc: string };
      setAccessToken(body.accessToken, body.expiresOnUtc);
      return true;
    } catch {
      clearAccessToken();
      return false;
    } finally {
      refreshInFlight = null;
    }
  })();

  return refreshInFlight;
}
