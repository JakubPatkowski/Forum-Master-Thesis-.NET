import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { contentApi } from "@/lib/api/content";
import { queryKeys } from "@/lib/api/keys";

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const EMPTY_PAGE = { items: [], nextCursor: null, hasMore: false };

describe("search sort (A/B v5: sort=newest|relevance travels on the wire and in the query key)", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    vi.stubGlobal("fetch", fetchMock);
    fetchMock.mockReset();
    fetchMock.mockImplementation(async () => jsonResponse(200, EMPTY_PAGE));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("sends sort=newest before the paging parameters when the page asks for it", async () => {
    await contentApi.searchThreads("kubernetes", null, 20, "newest");
    const url = String(fetchMock.mock.calls[0]?.[0]);
    expect(url).toContain("/api/content/search?q=kubernetes&sort=newest&limit=20");
  });

  it("passes the opaque cursor back verbatim together with the sort", async () => {
    await contentApi.searchThreads("kubernetes", "c3VydG9y", 20, "relevance");
    const url = String(fetchMock.mock.calls[0]?.[0]);
    expect(url).toContain("sort=relevance&cursor=c3VydG9y&limit=20");
  });

  it("omits the sort when the caller leaves the API default (relevance) alone", async () => {
    await contentApi.searchThreads("kubernetes", null, 5);
    const url = String(fetchMock.mock.calls[0]?.[0]);
    expect(url).toContain("/api/content/search?q=kubernetes&limit=5");
    expect(url).not.toContain("sort=");
  });

  it("keys the two orders separately (their cursors are not interchangeable)", () => {
    expect(queryKeys.search("q", "newest")).not.toEqual(queryKeys.search("q", "relevance"));
    expect(queryKeys.search("q")).toEqual(queryKeys.search("q", "relevance"));
    // Both stay under the realtime-covered "threads" root so a reconnect resync still reaches them.
    expect(queryKeys.search("q", "newest")[0]).toBe("threads");
  });
});
