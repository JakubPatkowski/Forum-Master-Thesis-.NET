/**
 * Client-side ordering + badge math for the conversation list — the ONE no-cursor,
 * hard-capped-at-200 list in the API. The server returns last-activity order but
 * documents it as unstable, so the client owns the sort: newest activity first,
 * never-messaged conversations last (newest-created first among themselves, which for
 * ULIDs is descending conversationId).
 */

import type { ConversationResponse } from "@/lib/api/types";

export function sortConversations(rows: ConversationResponse[]): ConversationResponse[] {
  return [...rows].sort((a, b) => {
    if (a.lastMessageOnUtc && b.lastMessageOnUtc) {
      const byActivity = b.lastMessageOnUtc.localeCompare(a.lastMessageOnUtc);
      if (byActivity !== 0) return byActivity;
    } else if (a.lastMessageOnUtc) {
      return -1;
    } else if (b.lastMessageOnUtc) {
      return 1;
    }
    return b.conversationId.localeCompare(a.conversationId);
  });
}

/**
 * The TopNav messages badge: sum of MY per-seat unread counts. Independent from the
 * notifications unread count — message arrivals never create notification rows.
 */
export function totalUnreadMessages(rows: ConversationResponse[]): number {
  return rows.reduce((sum, row) => sum + Math.max(0, row.unreadCount), 0);
}
