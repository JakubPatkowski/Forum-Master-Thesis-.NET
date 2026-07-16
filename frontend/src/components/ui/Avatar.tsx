"use client";

/**
 * User avatar: resolves the newest file attached with targetType=avatar for the user
 * (replace semantics server-side keep at most one). Falls back to the design's
 * monogram tile with cyan corner brackets when no avatar exists.
 */

import { useQuery } from "@tanstack/react-query";

import { CornerBrackets } from "@/components/ui/CornerBrackets";
import { filesApi } from "@/lib/api/files";
import { queryKeys } from "@/lib/api/keys";
import { staleTimes } from "@/lib/api/stale-times";

import styles from "./Avatar.module.css";

export function Avatar({
  userId,
  displayName,
  size = 34,
  brackets = false,
}: {
  userId: string;
  displayName: string;
  size?: number;
  brackets?: boolean;
}) {
  const files = useQuery({
    queryKey: queryKeys.filesByTarget("avatar", userId),
    queryFn: () => filesApi.listByTarget("avatar", userId),
    staleTime: staleTimes.presignedFiles,
  });

  const avatarUrl = files.data?.[0]?.url ?? null;
  const initial = (displayName.trim()[0] ?? "?").toUpperCase();

  return (
    <span className={styles.wrap} style={{ width: size, height: size }}>
      {avatarUrl ? (
        // eslint-disable-next-line @next/next/no-img-element
        <img
          className={styles.image}
          src={avatarUrl}
          alt={displayName}
          width={size}
          height={size}
        />
      ) : (
        <span
          className={styles.fallback}
          style={{ fontSize: Math.max(11, Math.round(size * 0.38)) }}
          aria-hidden
        >
          {initial}
        </span>
      )}
      {brackets ? (
        <CornerBrackets corners="two" size={Math.max(8, Math.round(size * 0.12))} />
      ) : null}
    </span>
  );
}
