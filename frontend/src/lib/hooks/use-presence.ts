"use client";

/**
 * Presence is the one social surface NOT covered by realtime push (backend design:
 * "presence never on the bus"), so it gets its own mechanism, separate from the
 * staleTime/push-invalidation pattern:
 *  - usePresenceHeartbeat(): POST /presence/heartbeat every ~30 s while the tab is
 *    VISIBLE (Page Visibility API pauses it when hidden) — mounted ONCE near the app
 *    root, next to the realtime connection lifecycle.
 *  - usePresence(ids): ONE batched GET /presence per view on a refetchInterval —
 *    active polling, never one request per row (the useReactionBatch shape).
 */

import { useEffect } from "react";
import { useQuery } from "@tanstack/react-query";

import { queryKeys } from "@/lib/api/keys";
import { socialApi } from "@/lib/api/social";
import type { PresenceStatus } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/auth-context";
import { presenceMap } from "@/lib/social/presence";

export const HEARTBEAT_INTERVAL_MS = 30_000;
export const PRESENCE_POLL_INTERVAL_MS = 25_000;

export function usePresenceHeartbeat() {
  const { isAuthenticated } = useAuth();

  useEffect(() => {
    if (!isAuthenticated) return undefined;

    let timer: ReturnType<typeof setInterval> | null = null;

    // Fire-and-forget: a missed beat only ages our own presence, never breaks the UI.
    const beat = () => void socialApi.heartbeat().catch(() => {});

    const start = () => {
      if (timer) return;
      beat();
      timer = setInterval(beat, HEARTBEAT_INTERVAL_MS);
    };
    const stop = () => {
      if (timer) clearInterval(timer);
      timer = null;
    };

    const onVisibility = () => {
      if (document.visibilityState === "visible") start();
      else stop();
    };

    onVisibility();
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      document.removeEventListener("visibilitychange", onVisibility);
      stop();
    };
  }, [isAuthenticated]);
}

/**
 * Poll presence for every user currently visible in the calling view (≤100). Returns
 * a Map — combine with statusOf() (missing entries read as offline). The interval
 * pauses while the window is unfocused (React Query's default), matching the
 * heartbeat's visibility-aware behavior.
 */
export function usePresence(userIds: string[]) {
  const { isAuthenticated } = useAuth();
  const ids = [...new Set(userIds)].sort().slice(0, 100);

  return useQuery({
    queryKey: queryKeys.presence(ids),
    queryFn: () => socialApi.getPresence(ids),
    enabled: isAuthenticated && ids.length > 0,
    refetchInterval: PRESENCE_POLL_INTERVAL_MS,
    select: (entries): Map<string, PresenceStatus> => presenceMap(entries),
  });
}
