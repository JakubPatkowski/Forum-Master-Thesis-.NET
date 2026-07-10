import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { RealtimeSocketManager, type RealtimeSocket } from "@/lib/realtime/socket-manager";

class FakeSocket implements RealtimeSocket {
  sent: string[] = [];
  closed = false;
  onopen: (() => void) | null = null;
  onclose: (() => void) | null = null;
  onerror: (() => void) | null = null;
  onmessage: ((event: { data: unknown }) => void) | null = null;

  send(data: string): void {
    this.sent.push(data);
  }

  close(): void {
    this.closed = true;
  }

  open(): void {
    this.onopen?.();
  }

  drop(): void {
    this.onclose?.();
  }

  receive(payload: unknown): void {
    this.onmessage?.({ data: JSON.stringify(payload) });
  }
}

describe("RealtimeSocketManager (brief §4.9 lifecycle)", () => {
  let sockets: FakeSocket[];
  let tickets: string[];
  let manager: RealtimeSocketManager;

  // Fake timers are active — flushing must advance the fake clock, not real setTimeout.
  const flush = () => vi.advanceTimersByTimeAsync(0);

  beforeEach(() => {
    vi.useFakeTimers();
    sockets = [];
    tickets = [];
    let ticketCounter = 0;
    manager = new RealtimeSocketManager({
      mintTicket: async () => {
        ticketCounter += 1;
        const ticket = `ticket-${ticketCounter}`;
        tickets.push(ticket);
        return ticket;
      },
      createSocket: () => {
        const socket = new FakeSocket();
        sockets.push(socket);
        return socket;
      },
      baseRetryDelayMs: 10,
    });
  });

  afterEach(() => {
    manager.stop();
    vi.useRealTimers();
  });

  it("mints a fresh ticket per connection attempt — never cached, never reused", async () => {
    manager.start();
    await flush();
    sockets[0]!.open();
    expect(tickets).toEqual(["ticket-1"]);

    sockets[0]!.drop();
    await vi.advanceTimersByTimeAsync(50);
    expect(tickets).toEqual(["ticket-1", "ticket-2"]);
    expect(sockets).toHaveLength(2);
  });

  it("replays every ref-counted subscription after reconnect and resends nothing twice", async () => {
    manager.start();
    await flush();
    sockets[0]!.open();

    manager.subscribe("category", "01CAT");
    manager.subscribe("category", "01CAT"); // second ref — no duplicate frame
    manager.subscribe("thread", "01THR");
    expect(sockets[0]!.sent.map((s) => JSON.parse(s))).toEqual([
      { action: "subscribe", view: "category", id: "01CAT" },
      { action: "subscribe", view: "thread", id: "01THR" },
    ]);

    sockets[0]!.drop();
    await vi.advanceTimersByTimeAsync(50);
    const second = sockets[1]!;
    second.open();
    expect(second.sent.map((s) => JSON.parse(s))).toEqual([
      { action: "subscribe", view: "category", id: "01CAT" },
      { action: "subscribe", view: "thread", id: "01THR" },
    ]);
  });

  it("unsubscribes only when the last reference releases", async () => {
    manager.start();
    await flush();
    sockets[0]!.open();

    manager.subscribe("thread", "01THR");
    manager.subscribe("thread", "01THR");
    manager.unsubscribe("thread", "01THR");
    expect(sockets[0]!.sent.filter((s) => s.includes("unsubscribe"))).toHaveLength(0);

    manager.unsubscribe("thread", "01THR");
    expect(sockets[0]!.sent.map((s) => JSON.parse(s))).toContainEqual({
      action: "unsubscribe",
      view: "thread",
      id: "01THR",
    });
  });

  it("emits connect (resync signal) on every successful (re)connect", async () => {
    const onConnect = vi.fn();
    manager.onConnect(onConnect);
    manager.start();
    await flush();
    sockets[0]!.open();
    expect(onConnect).toHaveBeenCalledTimes(1);

    sockets[0]!.drop();
    await vi.advanceTimersByTimeAsync(50);
    sockets[1]!.open();
    expect(onConnect).toHaveBeenCalledTimes(2);
  });

  it("dispatches change notifications and ignores control frames + garbage", async () => {
    const onNotification = vi.fn();
    manager.onNotification(onNotification);
    manager.start();
    await flush();
    const socket = sockets[0]!;
    socket.open();

    socket.receive({ type: "subscribed", view: "thread", id: "01THR" });
    socket.receive({ type: "error", reason: "forbidden-view" });
    socket.onmessage?.({ data: "not-json{{{" });
    socket.receive({
      type: "created",
      entity: "comment",
      id: "01C",
      parentId: "01T",
      categoryId: "01K",
    });

    expect(onNotification).toHaveBeenCalledTimes(1);
    expect(onNotification).toHaveBeenCalledWith(
      expect.objectContaining({ entity: "comment", type: "created" }),
    );
  });

  it("degrades status to offline after repeated failures but keeps retrying", async () => {
    const statuses: string[] = [];
    manager.onStatusChange((status) => statuses.push(status));
    manager.start();
    for (let i = 0; i < 8; i++) {
      await flush();
      const socket = sockets[sockets.length - 1];
      socket?.drop();
      await vi.advanceTimersByTimeAsync(20_000);
    }
    expect(statuses).toContain("offline");
    expect(sockets.length).toBeGreaterThan(6);
  });
});
