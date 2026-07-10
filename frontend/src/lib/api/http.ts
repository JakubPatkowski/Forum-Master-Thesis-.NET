/**
 * The one HTTP client every endpoint module goes through: base URL, JSON handling,
 * Authorization header injection, and the 401 → single-flight silent refresh → retry-once
 * interceptor (brief §4.2). All failures are thrown as `ApiError` (see problem.ts).
 */

import { apiUrl } from "@/lib/config";
import { ApiError, problemFromResponse } from "@/lib/api/problem";
import { getAccessToken, refreshAccessToken } from "@/lib/auth/token-store";

export interface ApiFetchOptions {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: unknown;
  /** Attach the bearer token when present (default true). */
  auth?: boolean;
  signal?: AbortSignal;
}

async function rawFetch(path: string, options: ApiFetchOptions, token: string | null) {
  const headers: Record<string, string> = {};
  if (options.body !== undefined) headers["Content-Type"] = "application/json";
  if (options.auth !== false && token) headers.Authorization = `Bearer ${token}`;

  return fetch(`${apiUrl}${path}`, {
    method: options.method ?? "GET",
    headers,
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
    // The refresh cookie is path-scoped to /api/identity; sending credentials on every
    // call is harmless and keeps the client simple.
    credentials: "include",
    signal: options.signal,
  });
}

export async function apiFetch<T = void>(path: string, options: ApiFetchOptions = {}): Promise<T> {
  let response: Response;
  try {
    response = await rawFetch(path, options, getAccessToken());
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") throw error;
    throw new ApiError(0, "Network error — is the API reachable?", null, "Network");
  }

  // On any 401: one silent refresh, one retry. A failed refresh is a hard logout —
  // clearAccessToken() (inside refreshAccessToken) notifies the AuthProvider.
  if (response.status === 401 && options.auth !== false && getAccessToken() !== null) {
    const refreshed = await refreshAccessToken();
    if (refreshed) {
      try {
        response = await rawFetch(path, options, getAccessToken());
      } catch (error) {
        if (error instanceof DOMException && error.name === "AbortError") throw error;
        throw new ApiError(0, "Network error — is the API reachable?", null, "Network");
      }
    }
  }

  if (!response.ok) {
    throw await problemFromResponse(response);
  }

  if (response.status === 204) return undefined as T;
  const text = await response.text();
  if (text === "") return undefined as T;
  return JSON.parse(text) as T;
}
