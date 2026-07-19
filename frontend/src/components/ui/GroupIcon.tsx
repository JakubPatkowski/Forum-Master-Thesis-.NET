"use client";

/**
 * Group icon: resolves the newest file attached with targetType=group_icon for the
 * group (replace semantics server-side keep at most one) and falls back to the
 * Monogram tile when none exists — a structural copy of CategoryIcon for the Social
 * module's groups. Group icons are anonymous-readable like avatars (no gate).
 */

import { useQuery } from "@tanstack/react-query";

import { Monogram } from "@/components/ui/Monogram";
import { filesApi } from "@/lib/api/files";
import { queryKeys } from "@/lib/api/keys";
import { staleTimes } from "@/lib/api/stale-times";
import type { MonogramTone } from "@/lib/utils/monogram";

import styles from "./GroupIcon.module.css";

export function GroupIcon({
  groupId,
  name,
  tone,
  active = false,
  size = 30,
}: {
  groupId: string;
  name: string;
  tone?: MonogramTone | "neutral";
  active?: boolean;
  size?: number;
}) {
  const files = useQuery({
    queryKey: queryKeys.filesByTarget("group_icon", groupId),
    queryFn: () => filesApi.listByTarget("group_icon", groupId),
    staleTime: staleTimes.presignedFiles,
  });

  const iconUrl = files.data?.[0]?.url ?? null;
  if (!iconUrl) {
    return <Monogram name={name} seed={groupId} tone={tone} active={active} size={size} />;
  }

  return (
    // eslint-disable-next-line @next/next/no-img-element
    <img className={styles.image} src={iconUrl} alt={name} width={size} height={size} />
  );
}
