import { describe, expect, it } from "vitest";

import type { ConversationResponse } from "@/lib/api/types";
import { sortConversations, totalUnreadMessages } from "@/lib/social/conversations";

function conversation(overrides: Partial<ConversationResponse>): ConversationResponse {
  return {
    conversationId: "01A",
    type: "direct",
    displayName: "someone",
    otherUserId: "01U",
    groupId: null,
    lastMessageId: null,
    lastMessagePreview: null,
    lastMessageSenderId: null,
    lastMessageOnUtc: null,
    unreadCount: 0,
    isMuted: false,
    ...overrides,
  };
}

describe("conversation list ordering (the one no-cursor, cap-200 list)", () => {
  it("sorts by last activity, newest first", () => {
    const rows = [
      conversation({ conversationId: "01A", lastMessageOnUtc: "2026-07-18T10:00:00Z" }),
      conversation({ conversationId: "01B", lastMessageOnUtc: "2026-07-18T12:00:00Z" }),
      conversation({ conversationId: "01C", lastMessageOnUtc: "2026-07-18T11:00:00Z" }),
    ];
    expect(sortConversations(rows).map((c) => c.conversationId)).toEqual(["01B", "01C", "01A"]);
  });

  it("puts never-messaged conversations last, newest-created (ULID desc) among themselves", () => {
    const rows = [
      conversation({ conversationId: "01AAA", lastMessageOnUtc: null }),
      conversation({ conversationId: "01ZZZ", lastMessageOnUtc: null }),
      conversation({ conversationId: "01BBB", lastMessageOnUtc: "2026-07-18T09:00:00Z" }),
    ];
    expect(sortConversations(rows).map((c) => c.conversationId)).toEqual([
      "01BBB",
      "01ZZZ",
      "01AAA",
    ]);
  });

  it("breaks last-activity ties on conversationId descending (stable, deterministic)", () => {
    const rows = [
      conversation({ conversationId: "01AAA", lastMessageOnUtc: "2026-07-18T10:00:00Z" }),
      conversation({ conversationId: "01ZZZ", lastMessageOnUtc: "2026-07-18T10:00:00Z" }),
    ];
    expect(sortConversations(rows).map((c) => c.conversationId)).toEqual(["01ZZZ", "01AAA"]);
  });

  it("does not mutate the input", () => {
    const rows = [
      conversation({ conversationId: "01A", lastMessageOnUtc: null }),
      conversation({ conversationId: "01B", lastMessageOnUtc: "2026-07-18T10:00:00Z" }),
    ];
    sortConversations(rows);
    expect(rows[0]!.conversationId).toBe("01A");
  });
});

describe("messages badge (independent from the notifications badge)", () => {
  it("sums per-conversation unread counts", () => {
    const rows = [
      conversation({ conversationId: "01A", unreadCount: 2 }),
      conversation({ conversationId: "01B", unreadCount: 0 }),
      conversation({ conversationId: "01C", unreadCount: 5 }),
    ];
    expect(totalUnreadMessages(rows)).toBe(7);
  });

  it("is zero for an empty list and ignores negative counts defensively", () => {
    expect(totalUnreadMessages([])).toBe(0);
    expect(totalUnreadMessages([conversation({ unreadCount: -3 })])).toBe(0);
  });
});
