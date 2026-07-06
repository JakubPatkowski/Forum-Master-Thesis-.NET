import { apiFetch } from "@/lib/api/http";
import type {
  ReactionSummaryResponse,
  ReactionTargetType,
  UserStatsResponse,
} from "@/lib/api/types";

export const engagementApi = {
  /** Idempotent both directions — re-liking / un-liking a no-op returns 200 with the current summary. */
  like: (targetType: ReactionTargetType, targetId: string) =>
    apiFetch<ReactionSummaryResponse>(`/api/engagement/reactions/${targetType}/${targetId}`, {
      method: "PUT",
    }),

  unlike: (targetType: ReactionTargetType, targetId: string) =>
    apiFetch<ReactionSummaryResponse>(`/api/engagement/reactions/${targetType}/${targetId}`, {
      method: "DELETE",
    }),

  getSummary: (targetType: ReactionTargetType, targetId: string) =>
    apiFetch<ReactionSummaryResponse>(`/api/engagement/reactions/${targetType}/${targetId}`),

  /**
   * Max 100 ids; zero-filled for unknown ids (NO existence check) — use strictly to
   * hydrate like counts onto an already-fetched list, never to validate ids.
   */
  getBatchSummary: (targetType: ReactionTargetType, targetIds: string[]) =>
    apiFetch<ReactionSummaryResponse[]>(
      `/api/engagement/reactions/batch?targetType=${targetType}&targetIds=${targetIds.join(",")}`,
    ),

  /** 404 only when the user id itself doesn't exist; zero-content users return zeros. */
  getUserStats: (userId: string) =>
    apiFetch<UserStatsResponse>(`/api/engagement/users/${userId}/stats`),
};
