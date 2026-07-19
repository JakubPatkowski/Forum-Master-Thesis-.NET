/** A social-row-shaped skeleton (avatar tile + two lines), matching FriendRow geometry. */

import { Skeleton } from "@/components/ui/Skeleton";

import styles from "./rows.module.css";

export function SocialRowSkeleton() {
  return (
    <div className={styles.skeletonRow} aria-hidden>
      <Skeleton width={34} height={34} />
      <div className={styles.skeletonLines}>
        <Skeleton width="55%" height={12} />
        <Skeleton width="35%" height={9} />
      </div>
    </div>
  );
}
