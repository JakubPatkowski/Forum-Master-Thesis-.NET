/**
 * Where an activity-log entry should navigate. Mirrors the parentId semantics used by
 * invalidation.ts: for comments parentId = the containing thread; for reactions
 * parentId = the containing thread when the reacted target is a comment, and null when
 * the reacted target is itself a thread (brief §4.9).
 */

import type { ChangeNotification } from "@/lib/api/types";

export function notificationHref(notification: ChangeNotification): string | undefined {
  switch (notification.entity) {
    case "thread":
      return `/t/${notification.id}`;
    case "comment":
      return notification.parentId
        ? `/t/${notification.parentId}#comment-${notification.id}`
        : undefined;
    case "reaction":
      return notification.parentId ? `/t/${notification.parentId}` : `/t/${notification.id}`;
  }
}
