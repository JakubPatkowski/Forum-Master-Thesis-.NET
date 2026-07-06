"use client";

/**
 * Counts `thread created` notifications relevant to the current feed view. New threads
 * never reorder the feed silently (brief §6) — they accumulate behind a LIVE banner and
 * load only when the reader clicks it.
 */

import { useCallback, useEffect, useState } from "react";

import { useRealtime } from "@/lib/realtime/realtime-context";

export function useNewThreadBanner(categoryFilter?: string) {
  const { addNotificationListener } = useRealtime();
  const [pendingCount, setPendingCount] = useState(0);

  useEffect(
    () =>
      addNotificationListener((notification) => {
        if (notification.entity !== "thread" || notification.type !== "created") return;
        if (categoryFilter && notification.categoryId !== categoryFilter) return;
        setPendingCount((count) => count + 1);
      }),
    [addNotificationListener, categoryFilter],
  );

  const clear = useCallback(() => setPendingCount(0), []);

  return { pendingCount, clear };
}
