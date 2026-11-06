"use client";

/**
 * Full-text search (design: Search.dc.html): debounced query → keyset-paged results
 * (same ThreadFeedItemResponse shape as the feed), batch like-count hydration, explicit
 * empty state, and the FILTERS · SOON note (category/tag/author filters need API params
 * that don't exist yet). Sort switch NEWEST / MOST RELEVANT (A/B v5): Newest is the default,
 * like Architecture B's search page; the choice travels in the URL (?sort=) so a shared link
 * reproduces it. Changing the sort starts a fresh query (the two orders have incompatible cursors).
 */

import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useMemo, useState } from "react";

import { useCompose } from "@/components/compose/compose-context";
import { PageShell } from "@/components/layout/PageShell";
import { ThreadCard } from "@/components/thread/ThreadCard";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { ApiErrorState } from "@/components/ui/ErrorState";
import { LoadMoreButton } from "@/components/ui/LoadMoreButton";
import { ThreadCardSkeleton } from "@/components/ui/Skeleton";
import { ApiError } from "@/lib/api/problem";
import type { SearchSort } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/auth-context";
import { useSearchThreads } from "@/lib/hooks/use-content";
import { useReactionBatch } from "@/lib/hooks/use-reactions";
import { parseSearchSort, searchUrl } from "@/lib/search/sort";

import styles from "./search.module.css";

const DEBOUNCE_MS = 350;

function SearchView() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const { isAuthenticated } = useAuth();
  const { openCreate } = useCompose();

  const initialQuery = searchParams.get("q") ?? "";
  const sort = parseSearchSort(searchParams.get("sort"));
  const [input, setInput] = useState(initialQuery);
  const [query, setQuery] = useState(initialQuery);

  // Debounce typing → query; keep the URL shareable.
  useEffect(() => {
    const handle = setTimeout(() => {
      const trimmed = input.trim();
      setQuery(trimmed);
      // Only rewrite the URL when the shareable query actually changed. router.replace to the
      // IDENTICAL url still issues an RSC round-trip (/search?_rsc=…) and re-renders — and with the
      // router in this effect's deps that re-render re-arms the effect: a steady self-refetch loop
      // that floods the network tab and leaves the page stuck in a pending transition (~15s stall).
      const current = searchParams.get("q") ?? "";
      if (trimmed !== current) {
        router.replace(searchUrl(trimmed, sort), { scroll: false });
      }
    }, DEBOUNCE_MS);
    return () => clearTimeout(handle);
  }, [input, router, searchParams, sort]);

  const setSort = (next: SearchSort) => {
    if (next !== sort) router.replace(searchUrl(query, next), { scroll: false });
  };

  const search = useSearchThreads(query, 20, sort);
  const results = useMemo(
    () => search.data?.pages.flatMap((page) => page.items) ?? [],
    [search.data],
  );
  const resultIds = useMemo(() => results.map((r) => r.id), [results]);
  const reactions = useReactionBatch("thread", resultIds);

  return (
    <div className={styles.wrap}>
      <div className={styles.searchBox}>
        <svg
          className={styles.searchIcon}
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.5"
        >
          <circle cx="11" cy="11" r="7" />
          <path d="M20.5 20.5 16 16" />
        </svg>
        <input
          className={styles.searchInput}
          value={input}
          onChange={(e) => setInput(e.target.value)}
          placeholder="Search threads…"
          autoFocus
          aria-label="Search threads"
        />
      </div>
      <div className={styles.metaRow}>
        <span className={styles.metaNote}>FULL-TEXT · KEYSET CURSOR · NO TOTAL COUNT</span>
        <div className={styles.sortSwitch} role="group" aria-label="Sort results" data-testid="search-sort">
          <button
            type="button"
            className={sort === "newest" ? styles.sortActive : styles.sortOption}
            aria-pressed={sort === "newest"}
            onClick={() => setSort("newest")}
          >
            NEWEST
          </button>
          <button
            type="button"
            className={sort === "relevance" ? styles.sortActive : styles.sortOption}
            aria-pressed={sort === "relevance"}
            onClick={() => setSort("relevance")}
          >
            MOST RELEVANT
          </button>
        </div>
        <span className={styles.metaFilters}>FILTERS</span>
        <Badge
          tone="warning"
          title="Category/tag/author filters need new API parameters — not in the contract yet"
        >
          SOON
        </Badge>
      </div>

      {query === "" ? (
        <EmptyState
          title="Search the forum"
          description="Full-text search over thread titles and bodies — titles rank higher."
        />
      ) : search.error instanceof ApiError ? (
        <ApiErrorState error={search.error} onRetry={() => void search.refetch()} />
      ) : search.isLoading ? (
        <div className={styles.list}>
          <ThreadCardSkeleton />
          <ThreadCardSkeleton />
          <ThreadCardSkeleton />
        </div>
      ) : results.length === 0 ? (
        <EmptyState
          icon={
            <svg
              width="20"
              height="20"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2.5"
            >
              <circle cx="11" cy="11" r="7" />
              <path d="M20.5 20.5 16 16" />
            </svg>
          }
          title={`No results for "${query}"`}
          description="Try different keywords, or start the thread yourself."
          action={
            isAuthenticated ? (
              <Button onClick={() => openCreate()}>Start a thread</Button>
            ) : undefined
          }
        />
      ) : (
        <>
          <div className={styles.resultsLabel}>
            <span className={styles.resultsBar} />
            <span>RESULTS FOR &quot;{query}&quot;</span>
          </div>
          <div className={styles.list}>
            {results.map((thread) => (
              <ThreadCard
                key={thread.id}
                thread={thread}
                reaction={reactions.data?.get(thread.id)}
              />
            ))}
          </div>
          <LoadMoreButton
            onClick={() => void search.fetchNextPage()}
            loading={search.isFetchingNextPage}
            hasMore={search.hasNextPage}
          />
        </>
      )}
    </div>
  );
}

export default function SearchPage() {
  return (
    <PageShell wide={false}>
      <Suspense>
        <SearchView />
      </Suspense>
    </PageShell>
  );
}
