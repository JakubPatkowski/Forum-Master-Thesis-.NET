"use client";

import styles from "./LoadMoreButton.module.css";

/**
 * The keyset-pagination trigger — cursor-driven "LOAD MORE ↓", never page numbers,
 * never a total count (none exists in the API).
 */
export function LoadMoreButton({
  onClick,
  loading = false,
  hasMore,
}: {
  onClick: () => void;
  loading?: boolean;
  hasMore: boolean;
}) {
  if (!hasMore) return null;
  return (
    <button className={styles.loadMore} onClick={onClick} disabled={loading}>
      {loading ? "LOADING…" : "LOAD MORE ↓"}
    </button>
  );
}
