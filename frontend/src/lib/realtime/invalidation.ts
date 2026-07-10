/**
 * Maps a change notification onto React Query cache invalidations. Notifications never
 * carry content — invalidation forces a re-fetch of exactly the affected entries
 * (fetch-then-patch, ADR 0010).
 *
 * Deliberate exception: `thread created` does NOT auto-invalidate feeds. The brief
 * requires new feed content to arrive behind a visible "N new threads" banner instead of
 * a silent reorder — feed pages listen for those notifications themselves and invalidate
 * on click (see useNewThreadBanner).
 */

import type { QueryClient } from "@tanstack/react-query";

import { queryKeys } from "@/lib/api/keys";
import type { ChangeNotification, ReactionTargetType } from "@/lib/api/types";

export function applyNotificationInvalidation(
  queryClient: QueryClient,
  notification: ChangeNotification,
): void {
  switch (notification.entity) {
    case "thread": {
      void queryClient.invalidateQueries({ queryKey: queryKeys.thread(notification.id) });
      if (notification.type !== "created" && notification.categoryId) {
        void queryClient.invalidateQueries({
          queryKey: queryKeys.threadFeed(notification.categoryId),
        });
      }
      break;
    }
    case "comment": {
      if (notification.parentId) {
        void queryClient.invalidateQueries({ queryKey: queryKeys.comments(notification.parentId) });
      }
      break;
    }
    case "reaction": {
      // parentId null → the reacted target is itself a thread; otherwise it's a comment
      // inside thread parentId (brief §4.9).
      const targetType: ReactionTargetType = notification.parentId ? "comment" : "thread";
      void queryClient.invalidateQueries({
        queryKey: queryKeys.reactions(targetType, notification.id),
      });
      // Batch summaries are keyed ["reactions","batch",targetType,ids[]] — invalidate
      // every batch that contains this target.
      void queryClient.invalidateQueries({
        predicate: (query) => {
          const key = query.queryKey;
          return (
            key[0] === "reactions" &&
            key[1] === "batch" &&
            key[2] === targetType &&
            Array.isArray(key[3]) &&
            (key[3] as string[]).includes(notification.id)
          );
        },
      });
      break;
    }
  }
}
