import { describe, expect, it } from "vitest";

import { mergeActivity, type ActivityMergeSource } from "@/lib/feed/activity-merge";

interface Item {
  id: string;
  createdOnUtc: string;
}

function item(id: string, createdOnUtc: string): Item {
  return { id, createdOnUtc };
}

function source(items: Item[], hasMore: boolean): ActivityMergeSource<Item> {
  return { items, hasMore };
}

describe("profile activity merge (threads + comments keyset feeds)", () => {
  it("interleaves exhausted sources fully, newest first", () => {
    const threads = source([item("T2", "2026-07-06T12:00:00Z"), item("T1", "2026-07-06T08:00:00Z")], false);
    const comments = source([item("C1", "2026-07-06T10:00:00Z")], false);

    const result = mergeActivity<Item>([threads, comments]);

    expect(result.visible.map((i) => i.id)).toEqual(["T2", "C1", "T1"]);
    expect(result.heldBack).toBe(0);
  });

  it("holds back items older than a refillable source's oldest loaded row", () => {
    // Comments still have unfetched pages; their oldest loaded row is 10:00 — the 08:00
    // thread must wait, or a later comment page (e.g. 09:00) would splice above it.
    const threads = source([item("T2", "2026-07-06T12:00:00Z"), item("T1", "2026-07-06T08:00:00Z")], false);
    const comments = source([item("C1", "2026-07-06T10:00:00Z")], true);

    const result = mergeActivity<Item>([threads, comments]);

    expect(result.visible.map((i) => i.id)).toEqual(["T2", "C1"]);
    expect(result.heldBack).toBe(1);
  });

  it("shows nothing while a refillable source has no loaded page yet", () => {
    const threads = source([item("T1", "2026-07-06T08:00:00Z")], false);
    const comments = source([], true);

    const result = mergeActivity<Item>([threads, comments]);

    expect(result.visible).toEqual([]);
    expect(result.heldBack).toBe(1);
  });

  it("breaks created-at ties by id descending, matching the backend keyset order", () => {
    const threads = source([item("01B", "2026-07-06T10:00:00Z")], false);
    const comments = source([item("01C", "2026-07-06T10:00:00Z"), item("01A", "2026-07-06T10:00:00Z")], false);

    const result = mergeActivity<Item>([threads, comments]);

    expect(result.visible.map((i) => i.id)).toEqual(["01C", "01B", "01A"]);
  });

  it("an empty exhausted source never blocks the other", () => {
    const threads = source([], false);
    const comments = source([item("C1", "2026-07-06T10:00:00Z")], false);

    const result = mergeActivity<Item>([threads, comments]);

    expect(result.visible.map((i) => i.id)).toEqual(["C1"]);
  });
});
