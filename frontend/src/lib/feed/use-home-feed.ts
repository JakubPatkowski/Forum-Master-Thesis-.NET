"use client";

/**
 * The home "All threads" feed — a client-side k-way merge over per-category keyset feeds
 * (see feed-merge.ts for why). Managed with local state rather than useInfiniteQuery:
 * the aggregate has N independent cursors, which doesn't fit a single infinite query;
 * realtime updates arrive via the new-thread banner → refresh() instead of cache
 * invalidation.
 */

import { useCallback, useEffect, useRef, useState } from "react";

import { contentApi } from "@/lib/api/content";
import type { ThreadFeedItemResponse } from "@/lib/api/types";
import { ApiError } from "@/lib/api/problem";
import {
  createSource,
  drainMerged,
  ingestPage,
  isExhausted,
  type FeedSourceState,
} from "@/lib/feed/feed-merge";

const PAGE_LIMIT = 10;
const BATCH_SIZE = 12;

interface HomeFeedState {
  items: ThreadFeedItemResponse[];
  pinned: ThreadFeedItemResponse[];
  hasMore: boolean;
  isLoading: boolean;
  isLoadingMore: boolean;
  error: ApiError | null;
}

const INITIAL: HomeFeedState = {
  items: [],
  pinned: [],
  hasMore: true,
  isLoading: true,
  isLoadingMore: false,
  error: null,
};

export function useHomeFeed(categoryIds: string[] | undefined) {
  const [state, setState] = useState<HomeFeedState>(INITIAL);
  const sourcesRef = useRef<FeedSourceState[]>([]);
  const pinnedRef = useRef<ThreadFeedItemResponse[]>([]);
  const generationRef = useRef(0);
  const busyRef = useRef(false);
  const categoriesKey = categoryIds?.join(",");

  const loadBatch = useCallback(async (generation: number, isFirst: boolean) => {
    if (busyRef.current) return;
    busyRef.current = true;
    setState((s) => ({ ...s, isLoading: isFirst, isLoadingMore: !isFirst, error: null }));
    try {
      let collected: ThreadFeedItemResponse[] = [];
      // Alternate fetch-and-drain until the batch is full or every source is exhausted.
      for (;;) {
        const drained = drainMerged(sourcesRef.current, BATCH_SIZE - collected.length);
        sourcesRef.current = drained.sources;
        collected = [...collected, ...drained.taken];
        if (generation !== generationRef.current) return;
        if (drained.blockedOn.length === 0) break;

        const pages = await Promise.all(
          drained.blockedOn.map(async (categoryId) => {
            const source = sourcesRef.current.find((s) => s.categoryId === categoryId);
            const cursor = source?.started ? source.nextCursor : null;
            return {
              categoryId,
              page: await contentApi.getThreadFeed(categoryId, cursor, PAGE_LIMIT),
            };
          }),
        );
        if (generation !== generationRef.current) return;

        for (const { categoryId, page } of pages) {
          const index = sourcesRef.current.findIndex((s) => s.categoryId === categoryId);
          const current = sourcesRef.current[index];
          if (!current) continue;
          const { source, pinned } = ingestPage(current, page);
          sourcesRef.current = [
            ...sourcesRef.current.slice(0, index),
            source,
            ...sourcesRef.current.slice(index + 1),
          ];
          pinnedRef.current = [...pinnedRef.current, ...pinned];
        }
      }

      const batch = collected;
      setState((s) => ({
        items: isFirst ? batch : [...s.items, ...batch],
        pinned: pinnedRef.current,
        hasMore: !isExhausted(sourcesRef.current),
        isLoading: false,
        isLoadingMore: false,
        error: null,
      }));
    } catch (error) {
      if (generation !== generationRef.current) return;
      setState((s) => ({
        ...s,
        isLoading: false,
        isLoadingMore: false,
        error:
          error instanceof ApiError
            ? error
            : new ApiError(0, "Failed to load the feed.", null, "Unknown"),
      }));
    } finally {
      busyRef.current = false;
    }
  }, []);

  const reset = useCallback(
    (ids: string[]) => {
      generationRef.current += 1;
      busyRef.current = false;
      sourcesRef.current = ids.map(createSource);
      pinnedRef.current = [];
      setState(INITIAL);
      if (ids.length === 0) {
        setState({ ...INITIAL, isLoading: false, hasMore: false });
        return;
      }
      void loadBatch(generationRef.current, true);
    },
    [loadBatch],
  );

  useEffect(() => {
    if (categoriesKey === undefined) return;
    reset(categoriesKey === "" ? [] : categoriesKey.split(","));
  }, [categoriesKey, reset]);

  const loadMore = useCallback(() => {
    void loadBatch(generationRef.current, false);
  }, [loadBatch]);

  /** Full reload — used by the "N new threads" live banner. */
  const refresh = useCallback(() => {
    if (categoriesKey === undefined) return;
    reset(categoriesKey === "" ? [] : categoriesKey.split(","));
  }, [categoriesKey, reset]);

  return { ...state, loadMore, refresh };
}
