/**
 * Owns THE single realtime WebSocket connection (brief §4.9).
 *
 * Lifecycle contract implemented here:
 *  - ticket-then-connect: a fresh single-use 30s ticket is minted immediately before
 *    every connection attempt — tickets are never cached or reused;
 *  - on ANY close/error, for any reason, the only move is: mint a fresh ticket and
 *    reconnect (exponential backoff). Failure causes are indistinguishable by design;
 *  - subscriptions are ref-counted per (view,id) and replayed after every reconnect;
 *  - every successful (re)connect emits "connect" so the app can resync (re-fetch) the
 *    data behind the current view — pushes carry no payloads, so anything missed while
 *    disconnected is only recoverable by re-fetching.
 *
 * Framework-agnostic and dependency-injected (ticket minting + socket factory) so the
 * reconnect/resubscribe bookkeeping is unit-testable without a browser socket.
 */

import type { RealtimeServerMessage, RealtimeViewKind } from "@/lib/api/types";
import { isChangeNotification } from "@/lib/api/types";
import type { ChangeNotification } from "@/lib/api/types";

export type RealtimeStatus = "offline" | "connecting" | "live" | "reconnecting";

/** The subset of the WebSocket surface the manager uses (widened so DOM WebSocket assigns). */
export interface RealtimeSocket {
  send(data: string): void;
  close(): void;
  onopen: ((...args: never[]) => unknown) | null;
  onclose: ((...args: never[]) => unknown) | null;
  onerror: ((...args: never[]) => unknown) | null;
  onmessage: ((event: { data: unknown }) => unknown) | null;
}

export interface SocketManagerOptions {
  mintTicket: () => Promise<string>;
  createSocket: (ticket: string) => RealtimeSocket;
  /** First-retry delay in ms (doubles per attempt, capped). Small in tests. */
  baseRetryDelayMs?: number;
  maxRetryDelayMs?: number;
  /** After this many consecutive failures the surfaced status degrades to "offline" (still retrying). */
  offlineAfterAttempts?: number;
}

interface SubscriptionEntry {
  view: RealtimeViewKind;
  id: string;
  refCount: number;
}

export class RealtimeSocketManager {
  private readonly options: Required<SocketManagerOptions>;
  private socket: RealtimeSocket | null = null;
  private started = false;
  private attempt = 0;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private statusValue: RealtimeStatus = "offline";
  private readonly subscriptions = new Map<string, SubscriptionEntry>();

  private readonly notificationListeners = new Set<(n: ChangeNotification) => void>();
  private readonly statusListeners = new Set<(s: RealtimeStatus) => void>();
  private readonly connectListeners = new Set<() => void>();

  constructor(options: SocketManagerOptions) {
    this.options = {
      baseRetryDelayMs: 1000,
      maxRetryDelayMs: 30_000,
      offlineAfterAttempts: 5,
      ...options,
    };
  }

  get status(): RealtimeStatus {
    return this.statusValue;
  }

  onNotification(listener: (n: ChangeNotification) => void): () => void {
    this.notificationListeners.add(listener);
    return () => this.notificationListeners.delete(listener);
  }

  onStatusChange(listener: (s: RealtimeStatus) => void): () => void {
    this.statusListeners.add(listener);
    return () => this.statusListeners.delete(listener);
  }

  /** Fires after every successful (re)connect, once subscriptions have been replayed. */
  onConnect(listener: () => void): () => void {
    this.connectListeners.add(listener);
    return () => this.connectListeners.delete(listener);
  }

  start(): void {
    if (this.started) return;
    this.started = true;
    this.attempt = 0;
    void this.connect();
  }

  stop(): void {
    this.started = false;
    if (this.retryTimer) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }
    const socket = this.socket;
    this.socket = null;
    if (socket) {
      socket.onclose = null;
      socket.onerror = null;
      socket.close();
    }
    this.setStatus("offline");
  }

  subscribe(view: RealtimeViewKind, id: string): void {
    const key = `${view}:${id}`;
    const existing = this.subscriptions.get(key);
    if (existing) {
      existing.refCount += 1;
      return;
    }
    this.subscriptions.set(key, { view, id, refCount: 1 });
    this.sendMessage({ action: "subscribe", view, id });
  }

  unsubscribe(view: RealtimeViewKind, id: string): void {
    const key = `${view}:${id}`;
    const existing = this.subscriptions.get(key);
    if (!existing) return;
    existing.refCount -= 1;
    if (existing.refCount > 0) return;
    this.subscriptions.delete(key);
    this.sendMessage({ action: "unsubscribe", view, id });
  }

  private sendMessage(message: { action: string; view: string; id: string }): void {
    if (!this.socket || this.statusValue !== "live") return;
    try {
      this.socket.send(JSON.stringify(message));
    } catch {
      // A dying socket surfaces through onclose — reconnect handles it.
    }
  }

  private setStatus(status: RealtimeStatus): void {
    if (this.statusValue === status) return;
    this.statusValue = status;
    for (const listener of this.statusListeners) listener(status);
  }

  private async connect(): Promise<void> {
    if (!this.started) return;
    this.setStatus(this.attempt === 0 ? "connecting" : "reconnecting");

    let ticket: string;
    try {
      ticket = await this.options.mintTicket();
    } catch {
      this.scheduleReconnect();
      return;
    }
    if (!this.started) return;

    let socket: RealtimeSocket;
    try {
      socket = this.options.createSocket(ticket);
    } catch {
      this.scheduleReconnect();
      return;
    }
    this.socket = socket;

    socket.onopen = () => {
      if (this.socket !== socket) return;
      this.attempt = 0;
      this.setStatus("live");
      // Replay every desired subscription — the server holds none across connections.
      for (const entry of this.subscriptions.values()) {
        this.sendMessage({ action: "subscribe", view: entry.view, id: entry.id });
      }
      for (const listener of this.connectListeners) listener();
    };

    const handleDrop = () => {
      if (this.socket !== socket) return;
      this.socket = null;
      this.scheduleReconnect();
    };
    socket.onclose = handleDrop;
    socket.onerror = handleDrop;

    socket.onmessage = (event) => {
      if (typeof event.data !== "string") return;
      let message: RealtimeServerMessage;
      try {
        message = JSON.parse(event.data) as RealtimeServerMessage;
      } catch {
        return;
      }
      if (isChangeNotification(message)) {
        for (const listener of this.notificationListeners) listener(message);
      }
      // Control frames (subscribed/unsubscribed acks, errors) need no handling: our
      // bookkeeping is optimistic, and error frames (forbidden-view etc.) are terminal
      // for that one subscribe — nothing sensible to retry client-side.
    };
  }

  private scheduleReconnect(): void {
    if (!this.started || this.retryTimer) return;
    this.attempt += 1;
    this.setStatus(this.attempt > this.options.offlineAfterAttempts ? "offline" : "reconnecting");
    const delay = Math.min(
      this.options.baseRetryDelayMs * 2 ** (this.attempt - 1),
      this.options.maxRetryDelayMs,
    );
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      void this.connect();
    }, delay);
  }
}
