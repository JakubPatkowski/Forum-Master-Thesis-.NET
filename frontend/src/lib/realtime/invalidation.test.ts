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
});
