"use client";

/**
 * LIVE ACTIVITY rail — real events from the realtime feed (entity.type rows with
 * elapsed time), plus the design's footer note about the fetch-then-patch contract.
 */

import { LiveDot } from "@/components/ui/LiveDot";
import { Panel } from "@/components/ui/Panel";
import { useRealtime } from "@/lib/realtime/realtime-context";
import { useAuth } from "@/lib/auth/auth-context";
import { timeAgoLabel } from "@/lib/utils/time";

import styles from "./panels.module.css";

export function LiveActivityPanel() {
  const { activity } = useRealtime();
  const { isAuthenticated } = useAuth();

  return (
    <Panel label="LIVE ACTIVITY" headerExtra={<LiveDot color="cyan" size={7} />}>
      <div className={styles.activityList}>
        {!isAuthenticated ? (
          <div className={styles.activityEmpty}>Log in to watch the change feed live.</div>
        ) : activity.length === 0 ? (
          <div className={styles.activityEmpty}>Quiet for now — events appear as they happen.</div>
        ) : (
          activity.slice(0, 6).map((entry, index) => (
            <div className={styles.activityRow} key={`${entry.receivedAt}-${index}`}>
              <span
                className={
                  entry.notification.entity === "reaction"
                    ? styles.squareAccent
                    : entry.notification.entity === "thread"
                      ? styles.squareCyan
                      : styles.squareNeutral
                }
              />
              <span className={styles.activityText}>
                {entry.notification.entity}.{entry.notification.type}
              </span>
              <span className={styles.activityTime}>
                {timeAgoLabel(new Date(entry.receivedAt).toISOString())}
              </span>
            </div>
          ))
        )}
      </div>
      <div className={styles.activityFooter}>WS NOTIFY → RE-FETCH · NO PAYLOADS</div>
    </Panel>
  );
}
