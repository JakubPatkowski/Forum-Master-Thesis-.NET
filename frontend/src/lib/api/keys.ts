import type { ReactionTargetType, FileTargetType } from "@/lib/api/types";

/**
 * Query-key factory — the single naming scheme for every server-state cache entry, so a
 * WebSocket notification can target exactly the entries it invalidates (see
 * lib/realtime/invalidation.ts).
 */
export const queryKeys = {
  categories: ["categories"] as const,
  category: (slug: string) => ["categories", slug] as const,

  threadFeed: (categoryId: string) => ["threads", "feed", categoryId] as const,
  thread: (threadId: string) => ["threads", "detail", threadId] as const,
  search: (q: string) => ["threads", "search", q] as const,

  comments: (threadId: string) => ["comments", threadId] as const,

  reactions: (targetType: ReactionTargetType, targetId: string) =>
    ["reactions", targetType, targetId] as const,
  reactionsBatch: (targetType: ReactionTargetType, targetIds: string[]) =>
    ["reactions", "batch", targetType, targetIds] as const,

  tagSuggestions: (query: string) => ["tags", "suggest", query] as const,

  userStats: (userId: string) => ["users", userId, "stats"] as const,
  userThreads: (userId: string) => ["users", userId, "threads"] as const,
  userComments: (userId: string) => ["users", userId, "comments"] as const,

  file: (fileId: string) => ["files", fileId] as const,
  filesByTarget: (targetType: FileTargetType, targetId: string) =>
    ["files", "target", targetType, targetId] as const,
};
