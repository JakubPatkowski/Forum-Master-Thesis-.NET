"use client";

/**
 * Session state for the whole app. The access token itself lives in lib/auth/token-store
 * (plain module memory); this provider derives the current user from it, restores the
 * session on first load via one silent refresh (the httpOnly cookie may still be valid),
 * and proactively refreshes shortly before the 15-minute expiry so the WS ticket flow
 * and long-lived tabs don't have to eat a 401 round-trip.
 */

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  useSyncExternalStore,
  type ReactNode,
} from "react";

import { identityApi } from "@/lib/api/identity";
import { decodeAccessToken, type CurrentUserClaims } from "@/lib/auth/jwt";
import {
  clearAccessToken,
  getTokenState,
  refreshAccessToken,
  setAccessToken,
  subscribe,
} from "@/lib/auth/token-store";

export interface AuthContextValue {
  /** null while the initial silent refresh is still running. */
  isRestoring: boolean;
  isAuthenticated: boolean;
  currentUser: CurrentUserClaims | null;
  /** Client-side UX heuristic only — real permission checks happen server-side per action. */
  isModerator: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (input: {
    username: string;
    email: string;
    displayName: string;
    password: string;
  }) => Promise<void>;
  logout: () => Promise<void>;
  logoutAll: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

/** Refresh this long before the access token expires (ms). */
const PROACTIVE_REFRESH_MARGIN_MS = 60_000;

export function AuthProvider({ children }: { children: ReactNode }) {
  const tokenState = useSyncExternalStore(subscribe, getTokenState, getTokenState);
  const [isRestoring, setIsRestoring] = useState(true);
  const refreshTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  // One-time session restore: the refresh cookie may outlive the tab.
  useEffect(() => {
    let cancelled = false;
    void refreshAccessToken().finally(() => {
      if (!cancelled) setIsRestoring(false);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  // Proactive refresh before expiry; the 401-retry interceptor remains the safety net.
  useEffect(() => {
    if (refreshTimer.current) clearTimeout(refreshTimer.current);
    if (!tokenState.token || !tokenState.expiresAt) return;

    const delay = Math.max(tokenState.expiresAt - Date.now() - PROACTIVE_REFRESH_MARGIN_MS, 5_000);
    refreshTimer.current = setTimeout(() => void refreshAccessToken(), delay);
    return () => {
      if (refreshTimer.current) clearTimeout(refreshTimer.current);
    };
  }, [tokenState]);

  const login = useCallback(async (email: string, password: string) => {
    const response = await identityApi.login({ email, password });
    setAccessToken(response.accessToken, response.expiresOnUtc);
  }, []);

  const register = useCallback(
    async (input: { username: string; email: string; displayName: string; password: string }) => {
      await identityApi.register(input);
      // The register endpoint doesn't log the user in — chain a login for a smooth flow.
      const response = await identityApi.login({ email: input.email, password: input.password });
      setAccessToken(response.accessToken, response.expiresOnUtc);
    },
    [],
  );

  const logout = useCallback(async () => {
    clearAccessToken();
    try {
      await identityApi.logout();
    } catch {
      // Logout is idempotent server-side; local state is already cleared.
    }
  }, []);

  const logoutAll = useCallback(async () => {
    try {
      await identityApi.logoutAll();
    } finally {
      clearAccessToken();
    }
  }, []);

  const value = useMemo<AuthContextValue>(() => {
    const currentUser = tokenState.token ? decodeAccessToken(tokenState.token) : null;
    return {
      isRestoring,
      isAuthenticated: currentUser !== null,
      currentUser,
      isModerator:
        currentUser !== null &&
        (currentUser.roles.includes("moderator") || currentUser.roles.includes("admin")),
      login,
      register,
      logout,
      logoutAll,
    };
  }, [tokenState, isRestoring, login, register, logout, logoutAll]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth must be used within AuthProvider");
  return context;
}
