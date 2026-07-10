import Link from "next/link";

import styles from "./TagChip.module.css";

/** Mono `#slug` chip. Clicking searches for the tag (no tag-filter endpoint exists yet). */
export function TagChip({ slug, onRemove }: { slug: string; onRemove?: () => void }) {
  if (onRemove) {
    return (
      <span className={styles.chipStatic}>
        #{slug}
        <button
          type="button"
          className={styles.remove}
          onClick={onRemove}
          aria-label={`Remove tag ${slug}`}
        >
          ×
        </button>
      </span>
    );
  }
  return (
    <Link className={styles.chip} href={`/search?q=${encodeURIComponent(slug)}`}>
      #{slug}
    </Link>
  );
}
