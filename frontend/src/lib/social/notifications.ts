/**
 * Presentation metadata for the closed durable-notification kind set. targetId
 * semantics per kind (verified against the backend's Notifier call sites):
 * friend.request / friend.accepted → friendshipId, group.invite → inviteId,
 * group.invite.accepted / group.kicked → groupId.
 */

import type { NotificationResponse } from "@/lib/api/types";

export interface NotificationMeta {
  /** Human line rendered after the actor's @username. */
  label: string;
  /** Where clicking the row navigates (undefined = not clickable). */
  href?: string;
  /** Row accent: cyan = social graph, accent = groups. */
  tone: "cyan" | "accent";
}

export function notificationMeta(notification: NotificationResponse): NotificationMeta {
  switch (notification.kind) {
    case "friend.request":
      return { label: "sent you a friend request", href: "/social?tab=requests", tone: "cyan" };
    case "friend.accepted":
      return { label: "accepted your friend request", href: "/social", tone: "cyan" };
    case "group.invite":
      return { label: "invited you to a group", href: "/social?tab=groups", tone: "accent" };
    case "group.invite.accepted":
      return {
        label: "accepted your group invite",
        href: notification.targetId ? `/social?group=${notification.targetId}` : "/social",
        tone: "accent",
      };
    case "group.kicked":
      return { label: "removed you from a group", href: "/social?tab=groups", tone: "accent" };
    default:
      // An unknown kind (future backend) still renders as a generic row.
      return { label: String(notification.kind).replaceAll(".", " "), tone: "cyan" };
  }
}
