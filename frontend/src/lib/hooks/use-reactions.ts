"use client";

/**
 * Reactions (likes). The toggle is optimistic and idempotent both directions per the
 * backend contract — no confirm dialog, no error state for the common no-op cases; the
 * server response (or an invalidation from the WS feed) settles the true count.
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { engagementApi } from "@/lib/api/engagement";
import { queryKeys } from "@/lib/api/keys";
import { staleTimes } from "@/lib/api/stale-times";
import type { ReactionSummaryResponse, ReactionTargetType } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/auth-context";

/**
 * One target's like summary. When a page-level useReactionBatch already covers this target
 * pass `covered: true`: the query then never fetches on its own (the batch's write-through
 * keeps its cache entry current), which is what stops a feed of N buttons from fanning out
 * into N single GETs alongside the one batch GET whenever both go stale together.
 */
export function useReactionSummary(
  targetType: ReactionTargetType,
  targetId: string,
  initial?: ReactionSummaryResponse,
  covered = false,
) {
  return useQuery({
    queryKey: queryKeys.reactions(targetType, targetId),
    queryFn: () => engagementApi.getSummary(targetType, targetId),
    initialData: initial,
    enabled: !covered,
    staleTime: staleTimes.realtimeCovered,
  });
}

/**
 * Batch like-count hydration for an already-fetched list (feed/search/comments). Zero-
 * filled for unknown ids — never used as an existence check. Max 100 ids per request.
 * Every landed batch is written through to the per-target cache entries, so `covered`
 * ReactionButtons re-render from here and the optimistic toggle starts from a fresh base.
 */
export function useReactionBatch(targetType: ReactionTargetType, targetIds: string[]) {
  const queryClient = useQueryClient();
  const ids = [...targetIds].sort();
  return useQuery({
    queryKey: queryKeys.reactionsBatch(targetType, ids),
    queryFn: async () => {
      const rows = await engagementApi.getBatchSummary(targetType, ids.slice(0, 100));
      for (const row of rows) {
        queryClient.setQueryData(queryKeys.reactions(targetType, row.targetId), row);
      }
      return rows;
    },
    enabled: ids.length > 0,
    staleTime: staleTimes.realtimeCovered,
    select: (rows): Map<string, ReactionSummaryResponse> =>
      new Map(rows.map((row) => [row.targetId, row])),
  });
}

export function useToggleReaction(targetType: ReactionTargetType, targetId: string) {
  const queryClient = useQueryClient();
  const { isAuthenticated } = useAuth();
  const key = queryKeys.reactions(targetType, targetId);

  const mutation = useMutation({
    mutationFn: (reacted: boolean) =>
      reacted
        ? engagementApi.unlike(targetType, targetId)
        : engagementApi.like(targetType, targetId),
    onMutate: async (reacted) => {
      await queryClient.cancelQueries({ queryKey: key });
      const previous = queryClient.getQueryData<ReactionSummaryResponse>(key);
      queryClient.setQueryData<ReactionSummaryResponse>(key, (current) => ({
        targetId,
        count: Math.max(0, (current?.count ?? 0) + (reacted ? -1 : 1)),
        viewerReacted: !reacted,
      }));
      return { previous };
    },
    onError: (_error, _reacted, context) => {
      if (context?.previous) queryClient.setQueryData(key, context.previous);
      else void queryClient.invalidateQueries({ queryKey: key });
    },
    onSuccess: (summary) => {
      queryClient.setQueryData(key, summary);
    },
  });

  return { toggle: mutation.mutate, isAuthenticated };
}
