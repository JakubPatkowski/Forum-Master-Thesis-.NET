"use client";

/**
 * One pending friend request. Incoming shows ACCEPT/DECLINE; outgoing shows the
 * PENDING chip + CANCEL (the same DELETE endpoint declines or cancels depending on
 * which side calls it).
 */

import Link from "next/link";

import { PresenceAvatar } from "@/components/social/PresenceAvatar";
import { Badge } from "@/components/ui/Badge";
import type { FriendRequestResponse } from "@/lib/api/types";
import { timeAgoLabel } from "@/lib/utils/time";

import styles from "./rows.module.css";

export function FriendRequestCard({
  request,
  direction,
  onAccept,
  onDecline,
  busy = false,
}: {
  request: FriendRequestResponse;
  direction: "incoming" | "outgoing";
  /** Incoming only. */
  onAccept?: () => void;
  /** DECLINE for incoming, CANCEL for outgoing. */
  onDecline: () => void;
  busy?: boolean;
}) {
  const otherId = direction === "incoming" ? request.requesterId : request.addresseeId;
  const otherName = direction === "incoming" ? request.requesterUsername : request.addresseeUsername;

  return (
    <div className={styles.card}>
      <div className={styles.cardHead}>
        <PresenceAvatar userId={otherId} username={otherName} />
        <span className={styles.text}>
          <Link href={`/u/${otherId}`} className={styles.name}>
            {otherName}
          </Link>
          <span className={styles.sub}>
            {direction === "incoming" ? "wants to be friends" : "request sent"} ·{" "}
            {timeAgoLabel(request.sentOnUtc)}
          </span>
        </span>
        {direction === "outgoing" ? <Badge tone="warning">PENDING</Badge> : null}
      </div>
      <div className={styles.cardActions}>
        {direction === "incoming" && onAccept ? (
          <button className={styles.accept} onClick={onAccept} disabled={busy}>
            ACCEPT
          </button>
        ) : null}
        <button className={styles.decline} onClick={onDecline} disabled={busy}>
          {direction === "incoming" ? "DECLINE" : "CANCEL"}
        </button>
      </div>
    </div>
  );
}
