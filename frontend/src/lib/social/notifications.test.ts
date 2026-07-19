import { describe, expect, it } from "vitest";

import type { NotificationResponse } from "@/lib/api/types";
import { notificationMeta } from "@/lib/social/notifications";

function notification(overrides: Partial<NotificationResponse>): NotificationResponse {
  return {
    notificationId: "01N",
    kind: "friend.request",
    actorId: "01A",
    actorUsername: "alice",
    targetId: null,
    isRead: false,
    createdOnUtc: "2026-07-18T10:00:00Z",
    ...overrides,
  };
}

describe("durable-notification presentation (closed 5-kind set)", () => {
  it("friend.request deep-links to the requests tab", () => {
    const meta = notificationMeta(notification({ kind: "friend.request" }));
    expect(meta.href).toBe("/social?tab=requests");
    expect(meta.tone).toBe("cyan");
  });

  it("group.invite deep-links to the groups tab (invites live there)", () => {
    const meta = notificationMeta(notification({ kind: "group.invite", targetId: "01I" }));
    expect(meta.href).toBe("/social?tab=groups");
    expect(meta.tone).toBe("accent");
  });

  it("group.invite.accepted deep-links to the group (targetId = groupId for this kind)", () => {
    const meta = notificationMeta(
      notification({ kind: "group.invite.accepted", targetId: "01G" }),
    );
    expect(meta.href).toBe("/social?group=01G");
  });

  it("renders every known kind with a non-empty label", () => {
    const kinds = [
      "friend.request",
      "friend.accepted",
      "group.invite",
      "group.invite.accepted",
      "group.kicked",
    ] as const;
    for (const kind of kinds) {
      expect(notificationMeta(notification({ kind })).label.length).toBeGreaterThan(0);
    }
  });

  it("degrades an unknown future kind to a generic, unclickable row", () => {
    const meta = notificationMeta(
      notification({ kind: "group.renamed" as NotificationResponse["kind"] }),
    );
    expect(meta.href).toBeUndefined();
    expect(meta.label).toBe("group renamed");
  });
});
