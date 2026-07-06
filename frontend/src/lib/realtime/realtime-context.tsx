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

export function RealtimeProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth();
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<RealtimeStatus>("offline");
  const [activity, setActivity] = useState<ActivityEntry[]>([]);
  const listenersRef = useRef(new Set<(n: ChangeNotification) => void>());

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
    // invisible until re-fetched, so every successful (re)connect resyncs the data
    // behind whatever is currently on screen.
    const offConnect = manager.onConnect(() => {
      void queryClient.invalidateQueries({ refetchType: "active" });
    });
    return () => {
      offStatus();
      offNotification();
      offConnect();
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
