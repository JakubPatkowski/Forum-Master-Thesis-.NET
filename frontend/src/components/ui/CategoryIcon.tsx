"use client";

/**
 * Category icon: resolves the newest file attached with targetType=category_icon for
 * the category (replace semantics server-side keep at most one) and falls back to the
 * Monogram tile when none exists — the same pattern Avatar uses for targetType=avatar.
 */

import { useQuery } from "@tanstack/react-query";

import { Monogram } from "@/components/ui/Monogram";
import { filesApi } from "@/lib/api/files";
import { queryKeys } from "@/lib/api/keys";
import { staleTimes } from "@/lib/api/stale-times";
import type { MonogramTone } from "@/lib/utils/monogram";

import styles from "./CategoryIcon.module.css";

export function CategoryIcon({
  categoryId,
  name,
  seed,
  tone,
  active = false,
  size = 30,
}: {
  categoryId: string;
  name: string;
  seed?: string;
  tone?: MonogramTone | "neutral";
  active?: boolean;
  size?: number;
}) {
  const files = useQuery({
    queryKey: queryKeys.filesByTarget("category_icon", categoryId),
    queryFn: () => filesApi.listByTarget("category_icon", categoryId),
    staleTime: staleTimes.presignedFiles,
  });

  const iconUrl = files.data?.[0]?.url ?? null;
  if (!iconUrl) {
    return <Monogram name={name} seed={seed} tone={tone} active={active} size={size} />;
  }

  return (
    // eslint-disable-next-line @next/next/no-img-element
    <img className={styles.image} src={iconUrl} alt={name} width={size} height={size} />
  );
}
