import styles from "./Skeleton.module.css";

/** Per-section loading placeholder (never a full-page spinner — brief §6). */
export function Skeleton({
  width,
  height = 12,
  className,
}: {
  width?: number | string;
  height?: number | string;
  className?: string;
}) {
  return (
    <span
      className={[styles.skeleton, className].filter(Boolean).join(" ")}
      style={{ width: width ?? "100%", height }}
      aria-hidden
    />
  );
}

/** A feed-row-shaped skeleton (tile + two lines), matching ThreadCard geometry. */
export function ThreadCardSkeleton() {
  return (
    <div className={styles.card} aria-hidden>
      <Skeleton width={44} height={44} />
      <div className={styles.lines}>
        <Skeleton width="35%" height={10} />
        <Skeleton width="72%" height={14} />
        <Skeleton width="50%" height={10} />
      </div>
    </div>
  );
}
