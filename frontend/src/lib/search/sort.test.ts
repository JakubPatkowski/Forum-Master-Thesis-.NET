import { describe, expect, it } from "vitest";

import { DEFAULT_SEARCH_SORT, parseSearchSort, searchUrl } from "@/lib/search/sort";

describe("search sort state (A/B v5: Newest is the page default, like B's search page)", () => {
  it("defaults to newest for a missing or unknown ?sort=", () => {
    expect(DEFAULT_SEARCH_SORT).toBe("newest");
    expect(parseSearchSort(null)).toBe("newest");
    expect(parseSearchSort(undefined)).toBe("newest");
    expect(parseSearchSort("oldest")).toBe("newest");
    expect(parseSearchSort("relevance")).toBe("relevance");
  });

  it("keeps the default sort out of the URL and encodes the query", () => {
    expect(searchUrl("", "newest")).toBe("/search");
    expect(searchUrl("kubernetes", "newest")).toBe("/search?q=kubernetes");
    expect(searchUrl("a b", "relevance")).toBe("/search?q=a+b&sort=relevance");
    expect(searchUrl("", "relevance")).toBe("/search?sort=relevance");
  });
});
