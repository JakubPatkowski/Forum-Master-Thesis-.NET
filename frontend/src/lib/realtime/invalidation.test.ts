import { QueryClient } from "@tanstack/react-query";
import { beforeEach, describe, expect, it } from "vitest";

import { queryKeys } from "@/lib/api/keys";
import { applyNotificationInvalidation } from "@/lib/realtime/invalidation";

describe("notification → cache invalidation mapping (fetch-then-patch)", () => {
  let queryClient: QueryClient;

  const seed = (key: readonly unknown[]) => {
    queryClient.setQueryData(key, { seeded: true });
  };

  const isInvalidated = (key: readonly unknown[]) =>
    queryClient.getQueryCache().find({ queryKey: key })?.state.isInvalidated ?? false;

  beforeEach(() => {
    queryClient = new QueryClient();
  });

  it("thread updated → invalidates the detail AND the category feed", () => {
    seed(queryKeys.thread("01T"));
    seed(queryKeys.threadFeed("01K"));
    applyNotificationInvalidation(queryClient, {
      type: "updated",
      entity: "thread",
      id: "01T",
      parentId: null,
      categoryId: "01K",
    });
    expect(isInvalidated(queryKeys.thread("01T"))).toBe(true);
    expect(isInvalidated(queryKeys.threadFeed("01K"))).toBe(true);
  });

  it("thread created → does NOT touch the feed (the LIVE banner owns that reload)", () => {
    seed(queryKeys.threadFeed("01K"));
    applyNotificationInvalidation(queryClient, {
      type: "created",
      entity: "thread",
      id: "01T",
      parentId: null,
      categoryId: "01K",
    });
    expect(isInvalidated(queryKeys.threadFeed("01K"))).toBe(false);
  });

  it("comment events → invalidate the containing thread's comment tree", () => {
    seed(queryKeys.comments("01T"));
    applyNotificationInvalidation(queryClient, {
      type: "created",
      entity: "comment",
      id: "01C",
      parentId: "01T",
      categoryId: "01K",
    });
    expect(isInvalidated(queryKeys.comments("01T"))).toBe(true);
  });

  it("reaction on a thread (parentId null) → invalidates the thread summary + containing batches", () => {
    seed(queryKeys.reactions("thread", "01T"));
    seed(queryKeys.reactionsBatch("thread", ["01A", "01T"]));
    seed(queryKeys.reactionsBatch("thread", ["01A", "01B"]));
    applyNotificationInvalidation(queryClient, {
      type: "created",
      entity: "reaction",
      id: "01T",
      parentId: null,
      categoryId: "01K",
    });
    expect(isInvalidated(queryKeys.reactions("thread", "01T"))).toBe(true);
    expect(isInvalidated(queryKeys.reactionsBatch("thread", ["01A", "01T"]))).toBe(true);
    expect(isInvalidated(queryKeys.reactionsBatch("thread", ["01A", "01B"]))).toBe(false);
  });

  it("reaction with parentId → targets the comment summary, not the thread's", () => {
    seed(queryKeys.reactions("comment", "01C"));
    seed(queryKeys.reactions("thread", "01C"));
    applyNotificationInvalidation(queryClient, {
      type: "deleted",
      entity: "reaction",
      id: "01C",
      parentId: "01T",
      categoryId: "01K",
    });
    expect(isInvalidated(queryKeys.reactions("comment", "01C"))).toBe(true);
    expect(isInvalidated(queryKeys.reactions("thread", "01C"))).toBe(false);
  });

  // --- Social (categoryId always null; parentId = the container) --------------

  it("friendship events → refresh both the requests split and the friends list", () => {
    seed(queryKeys.friendRequests);
    seed(queryKeys.friends);
    applyNotificationInvalidation(queryClient, {
      type: "updated",
      entity: "friendship",
      id: "01F",
      categoryId: null,
    });
    expect(isInvalidated(queryKeys.friendRequests)).toBe(true);
    expect(isInvalidated(queryKeys.friends)).toBe(true);
  });

  it("group events → invalidate the lists AND the detail under the shared root", () => {
    seed(queryKeys.groups("mine"));
    seed(queryKeys.group("01G"));
    applyNotificationInvalidation(queryClient, {
      type: "updated",
      entity: "group",
      id: "01G",
      parentId: "01G",
      categoryId: null,
    });
    expect(isInvalidated(queryKeys.groups("mine"))).toBe(true);
    expect(isInvalidated(queryKeys.group("01G"))).toBe(true);
  });

  it("group_member events → target the group's member list plus the groups root (counts)", () => {
    seed(queryKeys.groupMembers("01G"));
    seed(queryKeys.groupMembers("01H"));
    seed(queryKeys.groups("mine"));
    applyNotificationInvalidation(queryClient, {
      type: "created",
      entity: "group_member",
      id: "01U",
      parentId: "01G",
      categoryId: null,
    });
    expect(isInvalidated(queryKeys.groupMembers("01G"))).toBe(true);
    expect(isInvalidated(queryKeys.groupMembers("01H"))).toBe(false);
    expect(isInvalidated(queryKeys.groups("mine"))).toBe(true);
  });

  it("group_invite events → refresh my pending invites", () => {
    seed(queryKeys.groupInvites);
    applyNotificationInvalidation(queryClient, {
      type: "created",
      entity: "group_invite",
      id: "01I",
      parentId: "01G",
      categoryId: null,
    });
    expect(isInvalidated(queryKeys.groupInvites)).toBe(true);
  });

  it("message events → the conversation's history AND the list (previews, unread badges)", () => {
    seed(queryKeys.messages("01C"));
    seed(queryKeys.messages("01D"));
    seed(queryKeys.conversations);
    applyNotificationInvalidation(queryClient, {
      type: "created",
      entity: "message",
      id: "01M",
      parentId: "01C",
      categoryId: null,
    });
    expect(isInvalidated(queryKeys.messages("01C"))).toBe(true);
    expect(isInvalidated(queryKeys.messages("01D"))).toBe(false);
    expect(isInvalidated(queryKeys.conversations)).toBe(true);
  });

  it("notification events → both the bell list and the unread count under one root", () => {
    seed(queryKeys.notifications(false));
    seed(queryKeys.notificationUnreadCount);
    applyNotificationInvalidation(queryClient, {
      type: "created",
      entity: "notification",
      id: "01N",
      categoryId: null,
    });
    expect(isInvalidated(queryKeys.notifications(false))).toBe(true);
    expect(isInvalidated(queryKeys.notificationUnreadCount)).toBe(true);
  });
});
