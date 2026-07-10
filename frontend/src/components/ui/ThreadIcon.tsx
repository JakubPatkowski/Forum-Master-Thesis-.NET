"use client";

/**
 * Thread icon: resolves the newest file attached with targetType=thread_icon for the
 * thread (replace semantics server-side keep at most one) and falls back to the
 * category's icon — and, in turn, its Monogram tile — when the thread has none. Same
 * lazy-per-target pattern as CategoryIcon/Avatar; the fallback keeps every thread
 * visually anchored to its category until an author/moderator sets a dedicated icon.
 */

import { useQuery } from "@tanstack/react-query";

import { CategoryIcon } from "@/components/ui/CategoryIcon";
import { filesApi } from "@/lib/api/files";
import { queryKeys } from "@/lib/api/keys";

import styles from "./ThreadIcon.module.css";

export function ThreadIcon({
  threadId,
  categoryId,
  categoryName,
  categorySlug,
  size = 44,
}: {
  threadId: string;
  categoryId: string;
  categoryName: string;
  categorySlug?: string;
  size?: number;
}) {
  const files = useQuery({
    queryKey: queryKeys.filesByTarget("thread_icon", threadId),
    queryFn: () => filesApi.listByTarget("thread_icon", threadId),
    staleTime: 60_000,
  });

  const iconUrl = files.data?.[0]?.url ?? null;
  if (!iconUrl) {
    return (
      <CategoryIcon
        categoryId={categoryId}
        name={categoryName}
        seed={categorySlug}
        size={size}
      />
    );
  }

  return (
    // eslint-disable-next-line @next/next/no-img-element
    <img className={styles.image} src={iconUrl} alt="" width={size} height={size} />
  );
}
