/**
 * Two-source chronological merge for the profile activity timeline (a user's threads +
 * comments, each its own keyset feed). Same correctness invariant as feed-merge.ts: an
 * item may only be shown once no still-fetchable source could still produce something
 * newer than it — otherwise "Load more" on one source would splice older rows above
 * newer ones. Unlike the home feed's k-way streaming merge, both sources here are plain
 * useInfiniteQuery caches, so this recomputes the safe prefix from all loaded pages.
 */

export interface ActivityMergeSource<T> {
  /** All loaded items, newest-first (concatenated keyset pages). */
  items: readonly T[];
  /** Server says more pages exist after the loaded ones. */
  hasMore: boolean;
}

interface Timestamped {
  id: string;
  createdOnUtc: string;
}

function newerThan(a: Timestamped, b: Timestamped): boolean {
  const timeA = Date.parse(a.createdOnUtc);
  const timeB = Date.parse(b.createdOnUtc);
  if (timeA !== timeB) return timeA > timeB;
  // ULIDs are lexicographically time-ordered — same tie-break as the backend keyset.
  return a.id > b.id;
}

export interface ActivityMergeResult<T> {
  /** The safe-to-display slice, globally newest-first. */
  visible: T[];
  /** Loaded items held back until a blocking source fetches its next page. */
  heldBack: number;
}

export function mergeActivity<T extends Timestamped>(
  sources: ActivityMergeSource<T>[],
): ActivityMergeResult<T> {
  // Frontier = the newest "oldest loaded item" among refillable sources: anything older
  // might be preceded by rows still sitting in that source's unfetched pages. A refillable
  // source with nothing loaded yet blocks everything.
  let frontier: Timestamped | null = null;
  for (const source of sources) {
    if (!source.hasMore) continue;
    const oldest = source.items[source.items.length - 1];
    if (!oldest) return { visible: [], heldBack: sources.reduce((n, s) => n + s.items.length, 0) };
    if (!frontier || newerThan(oldest, frontier)) frontier = oldest;
  }

  const all = sources.flatMap((source) => [...source.items]);
  all.sort((a, b) => (newerThan(a, b) ? -1 : 1));

  if (!frontier) return { visible: all, heldBack: 0 };

  const boundary = frontier;
  const visible = all.filter((item) => item.id === boundary.id || !newerThan(boundary, item));
  return { visible, heldBack: all.length - visible.length };
}
