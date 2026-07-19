"use client";

/**
 * One entry of the IGNORED tab. A block is one-directional and invisible to the other
 * side (blocked interactions fail with the same generic 403 as privacy denials).
 */

import type { BlockedUserResponse } from "@/lib/api/types";
import { timeAgoLabel } from "@/lib/utils/time";

import styles from "./rows.module.css";

export function BlockedUserRow({
  user,
  onUnblock,
  busy = false,
}: {
  user: BlockedUserResponse;
  onUnblock: () => void;
  busy?: boolean;
}) {
  return (
    <div className={styles.row}>
      <span className={styles.iconButton} aria-hidden>
        <svg width="15" height="15" viewBox="0 0 24 24" fill="currentColor">
          <path d="M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20zm0 2c1.8 0 3.4.6 4.7 1.6L5.6 16.7A8 8 0 0 1 12 4zm0 16c-1.8 0-3.4-.6-4.7-1.6L18.4 7.3A8 8 0 0 1 12 20z" />
        </svg>
      </span>
      <span className={styles.text}>
        <span className={styles.name}>@{user.username}</span>
        <span className={styles.sub}>blocked {timeAgoLabel(user.blockedOnUtc)} ago</span>
      </span>
      <button
        className={`${styles.decline} ${styles.inlineAction}`}
        onClick={onUnblock}
        disabled={busy}
      >
        UNBLOCK
      </button>
    </div>
  );
}
