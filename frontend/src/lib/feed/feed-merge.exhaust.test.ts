import { describe, expect, it } from "vitest";

import { canProduce, createSource, drainMerged, exhaustSource, isExhausted, needsFetch } from "./feed-merge";

/**
 * Regression for the anonymous home page on a forum with private categories: the
 * category list contains categories whose feed answers 404/403 to this viewer (the A-12
 * read gate). Such a source must drop out of the merge instead of failing the whole feed.
 */
describe("exhaustSource", () => {
  it("turns an unread source into a finished one that never blocks the merge", () => {
    const privateSource = exhaustSource(createSource("private"));
    expect(privateSource.started).toBe(true);
    expect(privateSource.hasMore).toBe(false);
    expect(privateSource.nextCursor).toBeNull();
    expect(canProduce(privateSource)).toBe(false);
    expect(needsFetch(privateSource)).toBe(false);
  });

  it("lets the merge finish over the remaining sources", () => {
    const publicSource = {
      ...createSource("public"),
      started: true,
      hasMore: false,
      buffer: [
        { id: "b", createdOnUtc: "2026-01-02T00:00:00Z" },
        { id: "a", createdOnUtc: "2026-01-01T00:00:00Z" },
      ] as never[],
    };
    const sources = [publicSource, exhaustSource(createSource("private"))];
    const drained = drainMerged(sources, 10);
    expect(drained.taken.map((t) => (t as { id: string }).id)).toEqual(["b", "a"]);
    expect(drained.blockedOn).toEqual([]);
    expect(isExhausted(drained.sources)).toBe(true);
  });
});
