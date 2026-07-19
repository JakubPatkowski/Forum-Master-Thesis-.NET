"use client";

/**
 * Avatar with the design's corner presence dot. Presence is poll-derived
 * (lib/hooks/use-presence.ts) — pass the already-resolved status; omit it to render a
 * plain avatar (e.g. while the batch is still loading, or for privacy-hidden users).
 */

import { Avatar } from "@/components/ui/Avatar";
import type { PresenceStatus } from "@/lib/api/types";
import { presenceDotColor, presenceLabel } from "@/lib/social/presence";

import styles from "./rows.module.css";

const DOT_CLASS = {
  green: styles.dotGreen,
  amber: styles.dotAmber,
  red: styles.dotRed,
} as const;

export function PresenceAvatar({
  userId,
  username,
  status,
  size = 34,
}: {
  userId: string;
  username: string;
  status?: PresenceStatus;
  size?: number;
}) {
  return (
    <span className={styles.avatarWrap}>
      <Avatar userId={userId} displayName={username} size={size} />
      {status ? (
        <span
          className={`${styles.statusDot} ${DOT_CLASS[presenceDotColor(status)]}`}
          title={presenceLabel(status)}
        />
      ) : null}
    </span>
  );
}
