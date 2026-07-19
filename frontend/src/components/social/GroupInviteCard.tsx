"use client";

/** One pending group invite addressed to me: accept joins the group, decline deletes it. */

import { GroupIcon } from "@/components/ui/GroupIcon";
import type { GroupInviteResponse } from "@/lib/api/types";
import { timeAgoLabel } from "@/lib/utils/time";

import styles from "./rows.module.css";

export function GroupInviteCard({
  invite,
  onAccept,
  onDecline,
  busy = false,
}: {
  invite: GroupInviteResponse;
  onAccept: () => void;
  onDecline: () => void;
  busy?: boolean;
}) {
  return (
    <div className={styles.card}>
      <div className={styles.cardHead}>
        <GroupIcon groupId={invite.groupId} name={invite.groupName} size={34} />
        <span className={styles.text}>
          <span className={styles.name}>{invite.groupName}</span>
          <span className={styles.sub}>
            invited by @{invite.invitedByUsername} · {timeAgoLabel(invite.sentOnUtc)}
          </span>
        </span>
      </div>
      <div className={styles.cardActions}>
        <button className={styles.accept} onClick={onAccept} disabled={busy}>
          JOIN
        </button>
        <button className={styles.decline} onClick={onDecline} disabled={busy}>
          DECLINE
        </button>
      </div>
    </div>
  );
}
