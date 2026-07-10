"use client";

import { CornerBrackets } from "@/components/ui/CornerBrackets";
import { LiveDot } from "@/components/ui/LiveDot";

import styles from "./LiveBanner.module.css";

/**
 * The realtime arrival banner: new content is announced, never silently inserted
 * (brief §6 — "visibly patch the view"). Clicking loads the new content.
 */
export function LiveBanner({
  message,
  actionLabel = "LOAD NEW ↓",
  onAction,
}: {
  message: string;
  actionLabel?: string;
  onAction: () => void;
}) {
  return (
    <div className={styles.banner} role="status">
      <CornerBrackets size={10} />
      <LiveDot color="cyan" size={8} />
      <span className={styles.live}>LIVE</span>
      <span className={styles.message}>{message}</span>
      <button className={styles.action} onClick={onAction}>
        {actionLabel}
      </button>
    </div>
  );
}
