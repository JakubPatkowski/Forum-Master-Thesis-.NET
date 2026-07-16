"use client";

/**
 * App-level owner of the realtime connection: starts the socket manager while a user is
 * authenticated (anonymous users can't mint tickets), pipes change notifications into
 * React Query invalidation, resyncs active queries on every (re)connect, and keeps a
 * small rolling activity log for the LIVE ACTIVITY panel / notification bell.
 */

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { useQueryClient } from "@tanstack/react-query";

import { realtimeApi } from "@/lib/api/realtime";
import type { ChangeNotification, RealtimeViewKind } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/auth-context";
import { wsUrl } from "@/lib/config";
import { applyNotificationInvalidation } from "@/lib/realtime/invalidation";
import {
  RealtimeSocketManager,
  type RealtimeSocket,
  type RealtimeStatus,
} from "@/lib/realtime/socket-manager";

export interface ActivityEntry {
  notification: ChangeNotification;
  receivedAt: number;
}

interface RealtimeContextValue {
  status: RealtimeStatus;
  subscribe: (view: RealtimeViewKind, id: string) => () => void;
  /** Listen to raw notifications (for banners / NEW-glow bookkeeping). */
  addNotificationListener: (listener: (n: ChangeNotification) => void) => () => void;
  activity: ActivityEntry[];
}

const RealtimeContext = createContext<RealtimeContextValue | null>(null);

const ACTIVITY_LIMIT = 20;

// A reconnect flap that re-establishes a few times in a row coalesces into ONE resync within this window.
const RESYNC_DEBOUNCE_MS = 400;

// The resync's job is to restore exactly one invariant: "every push-invalidation that would have
// arrived while we were disconnected has been applied". Only the key families that
// applyNotificationInvalidation targets can violate it — categories/tags/files/user-stats get no
// pushes even while connected, so a reconnect says nothing about them and refetching them here
// (the previous "invalidate everything active") just burned rate-limiter budget on every flap.
const PUSH_COVERED_KEY_ROOTS = new Set<unknown>(["threads", "comments", "reactions"]);

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth();
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<RealtimeStatus>("offline");
  const [activity, setActivity] = useState<ActivityEntry[]>([]);
  const listenersRef = useRef(new Set<(n: ChangeNotification) => void>());
  const resyncTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const manager = useMemo(
    () =>
      new RealtimeSocketManager({
        mintTicket: async () => (await realtimeApi.createTicket()).ticket,
        // DOM WebSocket satisfies the structural subset (its handlers receive a
        // superset of { data }); the cast bridges TS's contravariant property check.
        createSocket: (ticket) =>
          new WebSocket(`${wsUrl}?ticket=${encodeURIComponent(ticket)}`) as RealtimeSocket,
      }),
    [],
  );

  useEffect(() => {
    const offStatus = manager.onStatusChange(setStatus);
    const offNotification = manager.onNotification((notification) => {
      applyNotificationInvalidation(queryClient, notification);
      setActivity((log) =>
        [{ notification, receivedAt: Date.now() }, ...log].slice(0, ACTIVITY_LIMIT),
      );
      for (const listener of listenersRef.current) listener(notification);
    });
    // Pushes carry no payloads: anything that changed while we were disconnected is
    // invisible until re-fetched, so every successful (re)connect resyncs the on-screen
    // push-covered queries (see PUSH_COVERED_KEY_ROOTS). Debounced so a reconnect flap
    // fires ONE refetch burst, not one per attempt (that burst is exactly what trips the
    // API rate limiter on a shared-IP session — the SIGNAL LOST feedback loop).
    const offConnect = manager.onConnect(() => {
      if (resyncTimer.current) clearTimeout(resyncTimer.current);
      resyncTimer.current = setTimeout(() => {
        void queryClient.invalidateQueries({
          refetchType: "active",
          predicate: (query) => PUSH_COVERED_KEY_ROOTS.has(query.queryKey[0]),
        });
      }, RESYNC_DEBOUNCE_MS);
    });
    return () => {
      offStatus();
      offNotification();
      offConnect();
      if (resyncTimer.current) clearTimeout(resyncTimer.current);
    };
  }, [manager, queryClient]);

  useEffect(() => {
    if (isAuthenticated) {
      manager.start();
      return () => manager.stop();
    }
    manager.stop();
    return undefined;
  }, [isAuthenticated, manager]);

  const subscribe = useCallback(
    (view: RealtimeViewKind, id: string) => {
      manager.subscribe(view, id);
      return () => manager.unsubscribe(view, id);
    },
    [manager],
  );

  const addNotificationListener = useCallback((listener: (n: ChangeNotification) => void) => {
    listenersRef.current.add(listener);
    return () => {
      listenersRef.current.delete(listener);
    };
  }, []);

  const value = useMemo<RealtimeContextValue>(
    () => ({ status, subscribe, addNotificationListener, activity }),
    [status, subscribe, addNotificationListener, activity],
  );

  return <RealtimeContext.Provider value={value}>{children}</RealtimeContext.Provider>;
}

export function useRealtime(): RealtimeContextValue {
  const context = useContext(RealtimeContext);
  if (!context) throw new Error("useRealtime must be used within RealtimeProvider");
  return context;
}

/** Subscribe to a realtime view for as long as the calling component is mounted. */
export function useRealtimeSubscription(view: RealtimeViewKind, id: string | null | undefined) {
  const { subscribe } = useRealtime();
  useEffect(() => {
    if (!id) return undefined;
    return subscribe(view, id);
  }, [subscribe, view, id]);
}
