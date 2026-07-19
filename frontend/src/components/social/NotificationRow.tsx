"use client";

/**
 * One durable bell notification (the closed 5-kind set — message arrivals never appear
 * here). Unread rows are highlighted; clicking navigates via the kind's deep link.
 */

import Link from "next/link";

import type { NotificationResponse } from "@/lib/api/types";
import { notificationMeta } from "@/lib/social/notifications";
import { timeAgoLabel } from "@/lib/utils/time";

import styles from "./rows.module.css";

export function NotificationRow({
  notification,
  onNavigate,
}: {
  notification: NotificationResponse;
  onNavigate?: () => void;
}) {
  const meta = notificationMeta(notification);
  const rowClass = notification.isRead ? styles.row : `${styles.row} ${styles.notifUnread}`;

  const content = (
    <>
      <span
        className={`${styles.notifSquare} ${meta.tone === "accent" ? styles.notifAccent : styles.notifCyan}`}
      />
      <span className={styles.notifText}>
        {notification.actorUsername ? (
          <span className={styles.notifActor}>@{notification.actorUsername} </span>
        ) : null}
        {meta.label}
      </span>
      <span className={styles.time}>{timeAgoLabel(notification.createdOnUtc)}</span>
    </>
  );

  return meta.href ? (
    <Link href={meta.href} className={rowClass} onClick={onNavigate}>
      {content}
    </Link>
  ) : (
    <div className={rowClass}>{content}</div>
  );
}
