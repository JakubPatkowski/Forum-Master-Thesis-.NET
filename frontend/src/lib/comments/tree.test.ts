import { describe, expect, it } from "vitest";

import { MAX_COMMENT_DEPTH, canReply, indentPx } from "@/lib/comments/tree";

describe("comment tree rules (brief §4.5)", () => {
  it("allows replies up to depth 4 and at the root", () => {
    expect(canReply({ depth: 0 })).toBe(true);
    expect(canReply({ depth: 4 })).toBe(true);
  });

  it("disables reply exactly at the max depth of 5", () => {
    expect(canReply({ depth: MAX_COMMENT_DEPTH })).toBe(false);
    expect(canReply({ depth: 6 })).toBe(false);
  });

  it("caps visual indentation so deep chains stay readable on narrow viewports", () => {
    expect(indentPx(0)).toBe(0);
    expect(indentPx(3)).toBe(78);
    expect(indentPx(5)).toBe(130);
    // depths beyond the cap (shouldn't happen server-side) don't push further right
    expect(indentPx(9)).toBe(130);
  });
});
