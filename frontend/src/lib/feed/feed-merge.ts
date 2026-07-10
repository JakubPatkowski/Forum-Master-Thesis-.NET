/**
 * K-way merge over per-category thread feeds.
 *
 * WHY THIS EXISTS: the backend has no global "all threads" endpoint —
 * GET /api/content/threads REQUIRES categoryId (feed.category_required 422 without it).
 * The home page's "All threads" view is therefore composed client-side: one keyset feed
 * per category, merged newest-first. Every per-category `nextCursor` remains an opaque
 * string passed back verbatim — the merge composes pages, it never touches cursors.
 *
 * Each category feed is sorted (is_pinned DESC, created_on_utc DESC, id DESC). Pinned
 * threads are split out on ingest (they surface in the dedicated PINNED rail), which
 * leaves every buffer strictly (createdOnUtc, id) descending — the invariant the k-way
 * merge needs. The merge only emits while every refillable source has a buffered head;
 * otherwise it reports which categories must fetch their next page first, so a slow
 * category can never cause newer items to be skipped.
 */

import type { CursorPage, ThreadFeedItemResponse } from "@/lib/api/types";

export interface FeedSourceState {
  categoryId: string;
  /** Non-pinned items, sorted newest-first — the merge buffer. */
  buffer: ThreadFeedItemResponse[];
  nextCursor: string | null;
  /** Server said more pages exist after nextCursor. */
  hasMore: boolean;
  /** First page fetched yet? */
  started: boolean;
}

export function createSource(categoryId: string): FeedSourceState {
  return { categoryId, buffer: [], nextCursor: null, hasMore: true, started: false };
}

/** True while this source may still produce items (buffered or fetchable). */
export function canProduce(source: FeedSourceState): boolean {
  return source.buffer.length > 0 || source.hasMore || !source.started;
}

/** True when the source is blocking the merge: nothing buffered but more is fetchable. */
export function needsFetch(source: FeedSourceState): boolean {
  return source.buffer.length === 0 && (source.hasMore || !source.started);
}

export interface IngestResult {
  source: FeedSourceState;
  pinned: ThreadFeedItemResponse[];
}

/** Folds a fetched page into the source, splitting pinned threads out of the buffer. */
export function ingestPage(
  source: FeedSourceState,
  page: CursorPage<ThreadFeedItemResponse>,
): IngestResult {
  const pinned = page.items.filter((t) => t.isPinned);
  const regular = page.items.filter((t) => !t.isPinned);
  return {
    source: {
      ...source,
      buffer: [...source.buffer, ...regular],
      nextCursor: page.nextCursor,
      hasMore: page.hasMore,
      started: true,
    },
    pinned,
  };
}

function newerThan(a: ThreadFeedItemResponse, b: ThreadFeedItemResponse): boolean {
  const timeA = Date.parse(a.createdOnUtc);
  const timeB = Date.parse(b.createdOnUtc);
  if (timeA !== timeB) return timeA > timeB;
  // ULIDs are lexicographically time-ordered — a stable tie-break matching the backend's
  // (created_on_utc DESC, id DESC) keyset order.
  return a.id > b.id;
}

export interface DrainResult {
  taken: ThreadFeedItemResponse[];
  sources: FeedSourceState[];
  /** Categories whose next page must be fetched before the merge can continue. */
  blockedOn: string[];
}

/**
 * Emits up to `count` globally-newest items across the buffers. Stops early when a
 * refillable source runs dry (correctness over eagerness) and reports it in `blockedOn`.
 */
export function drainMerged(sources: FeedSourceState[], count: number): DrainResult {
  const working = sources.map((s) => ({ ...s, buffer: [...s.buffer] }));
  const taken: ThreadFeedItemResponse[] = [];

  while (taken.length < count) {
    const blocked = working.filter(needsFetch).map((s) => s.categoryId);
    if (blocked.length > 0) {
      return { taken, sources: working, blockedOn: blocked };
    }

    let best: { source: FeedSourceState; head: ThreadFeedItemResponse } | null = null;
    for (const source of working) {
      const head = source.buffer[0];
      if (!head) continue;
      if (!best || newerThan(head, best.head)) {
        best = { source, head };
      }
    }
    if (!best) break; // everything exhausted

    best.source.buffer.shift();
    taken.push(best.head);
  }

  return { taken, sources: working, blockedOn: [] };
}

/** True when no source can produce anything anymore — the merged feed is complete. */
export function isExhausted(sources: FeedSourceState[]): boolean {
  return sources.every((s) => !canProduce(s));
}
