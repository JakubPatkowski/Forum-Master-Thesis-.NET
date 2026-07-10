"use client";

import { useQuery } from "@tanstack/react-query";

import { engagementApi } from "@/lib/api/engagement";
import { queryKeys } from "@/lib/api/keys";

export function useUserStats(userId: string | null | undefined) {
  return useQuery({
    queryKey: queryKeys.userStats(userId ?? "unknown"),
    queryFn: () => engagementApi.getUserStats(userId!),
    enabled: !!userId,
    staleTime: 30_000,
  });
}
