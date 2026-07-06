"use client";

/**
 * Reactions (likes). The toggle is optimistic and idempotent both directions per the
 * backend contract — no confirm dialog, no error state for the common no-op cases; the
 * server response (or an invalidation from the WS feed) settles the true count.
 */

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { engagementApi } from "@/lib/api/engagement";
import { queryKeys } from "@/lib/api/keys";
import type { ReactionSummaryResponse, ReactionTargetType } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/auth-context";

export function useReactionSummary(
  targetType: ReactionTargetType,
  targetId: string,
  initial?: ReactionSummaryResponse,
) {
  return useQuery({
    queryKey: queryKeys.reactions(targetType, targetId),
    queryFn: () => engagementApi.getSummary(targetType, targetId),
    initialData: initial,
    staleTime: 15_000,
  });
}

/**
 * Batch like-count hydration for an already-fetched list (feed/search/comments). Zero-
 * filled for unknown ids — never used as an existence check. Max 100 ids per request.
 */
export function useReactionBatch(targetType: ReactionTargetType, targetIds: string[]) {
  const ids = [...targetIds].sort();
  return useQuery({
    queryKey: queryKeys.reactionsBatch(targetType, ids),
    queryFn: () => engagementApi.getBatchSummary(targetType, ids.slice(0, 100)),
    enabled: ids.length > 0,
    staleTime: 15_000,
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
