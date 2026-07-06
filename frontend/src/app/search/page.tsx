"use client";

/**
 * Full-text search (design: Search.dc.html): debounced query → keyset-paged results
 * (same ThreadFeedItemResponse shape as the feed), batch like-count hydration, explicit
 * empty state, and the FILTERS · SOON note (category/tag/author filters need API params
 * that don't exist yet).
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
import { useAuth } from "@/lib/auth/auth-context";
import { useSearchThreads } from "@/lib/hooks/use-content";
import { useReactionBatch } from "@/lib/hooks/use-reactions";

import styles from "./search.module.css";

const DEBOUNCE_MS = 350;

function SearchView() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const { isAuthenticated } = useAuth();
  const { openCreate } = useCompose();

  const initialQuery = searchParams.get("q") ?? "";
  const [input, setInput] = useState(initialQuery);
  const [query, setQuery] = useState(initialQuery);

  // Debounce typing → query; keep the URL shareable.
  useEffect(() => {
    const handle = setTimeout(() => {
      const trimmed = input.trim();
      setQuery(trimmed);
      const url = trimmed ? `/search?q=${encodeURIComponent(trimmed)}` : "/search";
      router.replace(url, { scroll: false });
    }, DEBOUNCE_MS);
    return () => clearTimeout(handle);
  }, [input, router]);

  const search = useSearchThreads(query);
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
