import type { GroupListFilter, ReactionTargetType, FileTargetType } from "@/lib/api/types";

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

  // Social. Root names matter: everything realtime-covered is listed in
  // PUSH_COVERED_KEY_ROOTS (lib/realtime/realtime-context.tsx) so reconnects resync it.
  // presence/privacy/blocks are deliberately NOT push-covered (polling / mutation-only).
  friends: ["friends"] as const,
  friendRequests: ["friendRequests"] as const,
  blocks: ["blocks"] as const,
  groups: (filter: GroupListFilter) => ["groups", "list", filter] as const,
  group: (groupId: string) => ["groups", "detail", groupId] as const,
  groupMembers: (groupId: string) => ["groupMembers", groupId] as const,
  groupInvites: ["groupInvites"] as const,
  conversations: ["conversations"] as const,
  messages: (conversationId: string) => ["messages", conversationId] as const,
  notifications: (unreadOnly: boolean) => ["notifications", "list", unreadOnly] as const,
  notificationUnreadCount: ["notifications", "unread-count"] as const,
  privacy: ["privacy"] as const,
  presence: (userIds: string[]) => ["presence", userIds] as const,
};
