import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import { queryKeys } from "@/lib/api/keys";
import type { ReactionSummaryResponse } from "@/lib/api/types";
import { useReactionBatch, useReactionSummary } from "@/lib/hooks/use-reactions";

vi.mock("@/lib/api/engagement", () => ({
  engagementApi: {
    getSummary: vi.fn(),
    getBatchSummary: vi.fn(),
    like: vi.fn(),
    unlike: vi.fn(),
  },
}));

vi.mock("@/lib/auth/auth-context", () => ({
  useAuth: () => ({ isAuthenticated: true }),
}));

import { engagementApi } from "@/lib/api/engagement";

const getSummary = vi.mocked(engagementApi.getSummary);
const getBatchSummary = vi.mocked(engagementApi.getBatchSummary);

function summary(targetId: string, count: number): ReactionSummaryResponse {
  return { targetId, count, viewerReacted: false };
}

/**
 * The 10d over-fetch fix: a page-level batch is the ONLY fetch for the targets it covers.
 * Covered per-target queries render from the batch's write-through instead of issuing the
 * N single GETs that used to accompany every batch refetch (179 singles vs 77 batches in
 * 24h of live traffic).
 */
describe("reaction batch/single coordination", () => {
  let client: QueryClient;

  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );

  beforeEach(() => {
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    vi.clearAllMocks();
  });

  it("write-through: a landed batch fills every per-target cache entry", async () => {
    getBatchSummary.mockResolvedValue([summary("t1", 3), summary("t2", 0)]);

    const { result } = renderHook(() => useReactionBatch("thread", ["t2", "t1"]), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(client.getQueryData(queryKeys.reactions("thread", "t1"))).toEqual(summary("t1", 3));
    expect(client.getQueryData(queryKeys.reactions("thread", "t2"))).toEqual(summary("t2", 0));
    expect(getBatchSummary).toHaveBeenCalledTimes(1);
  });

  it("covered summaries never fetch on their own, even without initial data", async () => {
    getBatchSummary.mockResolvedValue([summary("t1", 5)]);

    const single = renderHook(
      () => useReactionSummary("thread", "t1", undefined, /* covered */ true),
      { wrapper },
    );
    const batch = renderHook(() => useReactionBatch("thread", ["t1"]), { wrapper });

    await waitFor(() => expect(batch.result.current.isSuccess).toBe(true));
    // The covered button re-renders from the batch's write-through...
    await waitFor(() => expect(single.result.current.data).toEqual(summary("t1", 5)));
    // ...and the single-summary endpoint was never touched.
    expect(getSummary).not.toHaveBeenCalled();
  });

  it("uncovered summaries still fetch standalone (thread-detail main button)", async () => {
    getSummary.mockResolvedValue(summary("t9", 7));

    const { result } = renderHook(() => useReactionSummary("thread", "t9"), { wrapper });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual(summary("t9", 7));
    expect(getSummary).toHaveBeenCalledTimes(1);
  });
});
