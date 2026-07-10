import { describe, expect, it } from "vitest";

import type { CursorPage, ThreadFeedItemResponse } from "@/lib/api/types";
import {
  createSource,
  drainMerged,
  ingestPage,
  isExhausted,
  needsFetch,
} from "@/lib/feed/feed-merge";

let counter = 0;

function thread(
  categoryId: string,
  createdOnUtc: string,
  overrides: Partial<ThreadFeedItemResponse> = {},
): ThreadFeedItemResponse {
  counter += 1;
  return {
    id: overrides.id ?? `01THREAD${String(counter).padStart(18, "0")}`,
    categoryId,
    categorySlug: categoryId,
    categoryName: categoryId,
    title: `t${counter}`,
    isPinned: false,
    ownerId: "01OWNER",
    username: "u",
    displayName: "U",
    likeCount: 0,
    commentCount: 0,
    createdOnUtc,
    lastModifiedOnUtc: null,
    ...overrides,
  };
}

function page(
  items: ThreadFeedItemResponse[],
  hasMore = false,
  nextCursor: string | null = null,
): CursorPage<ThreadFeedItemResponse> {
  return { items, nextCursor, hasMore };
}

describe("home-feed k-way merge (no global feed endpoint exists — brief §4.4)", () => {
  it("splits pinned threads out of the merge buffer on ingest", () => {
    const source = createSource("A");
    const pinnedThread = thread("A", "2026-07-06T10:00:00Z", { isPinned: true });
    const regular = thread("A", "2026-07-06T09:00:00Z");
    const { source: next, pinned } = ingestPage(source, page([pinnedThread, regular]));
    expect(pinned).toEqual([pinnedThread]);
    expect(next.buffer).toEqual([regular]);
    expect(next.started).toBe(true);
  });

  it("merges buffered items globally newest-first", () => {
    let a = createSource("A");
    let b = createSource("B");
    const a1 = thread("A", "2026-07-06T12:00:00Z");
    const a2 = thread("A", "2026-07-06T10:00:00Z");
    const b1 = thread("B", "2026-07-06T11:00:00Z");
    a = ingestPage(a, page([a1, a2])).source;
    b = ingestPage(b, page([b1])).source;

    const result = drainMerged([a, b], 10);
    expect(result.taken.map((t) => t.id)).toEqual([a1.id, b1.id, a2.id]);
    expect(result.blockedOn).toEqual([]);
  });

  it("stops and reports a refillable source that ran dry instead of skipping ahead", () => {
    let a = createSource("A");
    let b = createSource("B");
    const a1 = thread("A", "2026-07-06T12:00:00Z");
    // B has more pages server-side — its next item could be newer than a2.
    a = ingestPage(a, page([a1, thread("A", "2026-07-06T08:00:00Z")])).source;
    b = ingestPage(b, page([thread("B", "2026-07-06T11:00:00Z")], true, "opaque-cursor")).source;

    const result = drainMerged([a, b], 10);
    // Takes a1 (12:00) and B's 11:00 item, then must stop: B is empty but hasMore.
    expect(result.taken).toHaveLength(2);
    expect(result.blockedOn).toEqual(["B"]);
  });

  it("treats an unfetched source as blocking (started=false)", () => {
    const a = ingestPage(createSource("A"), page([thread("A", "2026-07-06T12:00:00Z")])).source;
    const b = createSource("B");
    expect(needsFetch(b)).toBe(true);
    const result = drainMerged([a, b], 10);
    expect(result.taken).toHaveLength(0);
    expect(result.blockedOn).toEqual(["B"]);
  });

  it("breaks createdOnUtc ties by id descending (matches the backend keyset order)", () => {
    const t = "2026-07-06T12:00:00Z";
    const older = thread("A", t, { id: "01AAAAAAAAAAAAAAAAAAAAAAAA" });
    const newer = thread("B", t, { id: "01ZZZZZZZZZZZZZZZZZZZZZZZZ" });
    const a = ingestPage(createSource("A"), page([older])).source;
    const b = ingestPage(createSource("B"), page([newer])).source;
    const result = drainMerged([a, b], 10);
    expect(result.taken.map((x) => x.id)).toEqual([newer.id, older.id]);
  });

  it("is exhausted only when no source can produce", () => {
    const a = ingestPage(createSource("A"), page([])).source;
    const b = ingestPage(createSource("B"), page([], true, "c")).source;
    expect(isExhausted([a])).toBe(true);
    expect(isExhausted([a, b])).toBe(false);
  });
});
