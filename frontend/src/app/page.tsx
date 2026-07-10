"use client";

/**
 * Home / "All threads" (design: Home.dc.html). The global feed is a client-side k-way
 * merge over per-category keyset feeds (no global endpoint exists — see
 * lib/feed/feed-merge.ts). Realtime: subscribes to every category view; new threads
 * accumulate behind the LIVE banner (never a silent reorder) and load on click.
 */

import { useEffect, useMemo } from "react";

import { CategorySidebar } from "@/components/layout/CategorySidebar";
import { PageShell } from "@/components/layout/PageShell";
import { LiveActivityPanel } from "@/components/panels/LiveActivityPanel";
import { PopularTagsPanel } from "@/components/panels/PopularTagsPanel";
import { ThreadCard } from "@/components/thread/ThreadCard";
import { useCompose } from "@/components/compose/compose-context";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { GenericErrorState } from "@/components/ui/ErrorState";
import { LiveBanner } from "@/components/ui/LiveBanner";
import { LoadMoreButton } from "@/components/ui/LoadMoreButton";
import { ThreadCardSkeleton } from "@/components/ui/Skeleton";
import { useAuth } from "@/lib/auth/auth-context";
import { useHomeFeed } from "@/lib/feed/use-home-feed";
import { useCategories } from "@/lib/hooks/use-content";
import { useNewThreadBanner } from "@/lib/hooks/use-new-thread-banner";
import { useReactionBatch } from "@/lib/hooks/use-reactions";
import { useRealtime } from "@/lib/realtime/realtime-context";

import styles from "./home.module.css";

/** Stay well under the 64-subscription cap even on a category-heavy forum. */
const MAX_CATEGORY_SUBSCRIPTIONS = 32;

export default function HomePage() {
  const { isAuthenticated } = useAuth();
  const { subscribe } = useRealtime();
  const { openCreate } = useCompose();
  const categories = useCategories();

  const categoryIds = useMemo(
    () => categories.data?.map((category) => category.id),
    [categories.data],
  );
  const feed = useHomeFeed(categoryIds);
  const banner = useNewThreadBanner();

  // Home shows every category at once, so it subscribes per category (the "subscribe
  // per open view" rule — the open view here IS the union of categories).
  useEffect(() => {
    if (!categoryIds || !isAuthenticated) return;
    const unsubscribers = categoryIds
      .slice(0, MAX_CATEGORY_SUBSCRIPTIONS)
      .map((id) => subscribe("category", id));
    return () => unsubscribers.forEach((unsubscribe) => unsubscribe());
  }, [categoryIds, isAuthenticated, subscribe]);

  const visiblePinned = feed.pinned.slice(0, 2);
  const allIds = useMemo(
    () => [...visiblePinned.map((t) => t.id), ...feed.items.map((t) => t.id)],
    [visiblePinned, feed.items],
  );
  const reactions = useReactionBatch("thread", allIds);

  const loadNew = () => {
    banner.clear();
    feed.refresh();
  };

  return (
    <PageShell>
      <div className={styles.grid}>
        <aside className={styles.left}>
          <CategorySidebar />
        </aside>

        <main className={styles.main}>
          {banner.pendingCount > 0 ? (
            <LiveBanner
              message={
                banner.pendingCount === 1
                  ? "1 new thread arrived while you were reading"
                  : `${banner.pendingCount} new threads arrived while you were reading`
              }
              onAction={loadNew}
            />
          ) : null}

          {visiblePinned.length > 0 ? (
            <section>
              <div className={styles.sectionLabel}>
                <svg width="13" height="13" viewBox="0 0 24 24" fill="var(--color-accent-base)">
                  <path d="M14 3h-4v2h1v5l-3 3v2h4v6l1 1 1-1v-6h4v-2l-3-3V5h1V3z" />
                </svg>
                <span>PINNED</span>
              </div>
              <div className={styles.list}>
                {visiblePinned.map((thread) => (
                  <ThreadCard
                    key={thread.id}
                    thread={thread}
                    reaction={reactions.data?.get(thread.id)}
                  />
                ))}
              </div>
            </section>
          ) : null}

          <section>
            <div className={styles.feedHeader}>
              <div>
                <h1 className={styles.feedTitle}>Latest threads</h1>
                <div className={styles.feedNote}>NEWEST FIRST · CURSOR PAGED</div>
              </div>
              {isAuthenticated ? (
                <Button onClick={() => openCreate()}>+ New thread</Button>
              ) : (
                <a href="/auth" className={styles.loginCta}>
                  Log in to post →
                </a>
              )}
            </div>

            {feed.error ? (
              <GenericErrorState onRetry={feed.refresh} detail={feed.error.title} />
            ) : feed.isLoading ? (
              <div className={styles.list}>
                <ThreadCardSkeleton />
                <ThreadCardSkeleton />
                <ThreadCardSkeleton />
                <ThreadCardSkeleton />
              </div>
            ) : feed.items.length === 0 && visiblePinned.length === 0 ? (
              <EmptyState
                title="No threads yet"
                description="Be the first to start a discussion."
                action={
                  isAuthenticated ? (
                    <Button onClick={() => openCreate()}>Start a thread</Button>
                  ) : undefined
                }
              />
            ) : (
              <>
                <div className={styles.list}>
                  {feed.items.map((thread) => (
                    <ThreadCard
                      key={thread.id}
                      thread={thread}
                      reaction={reactions.data?.get(thread.id)}
                    />
                  ))}
                </div>
                <LoadMoreButton
                  onClick={feed.loadMore}
                  loading={feed.isLoadingMore}
                  hasMore={feed.hasMore}
                />
              </>
            )}
          </section>
        </main>

        <aside className={styles.right}>
          <PopularTagsPanel />
          <LiveActivityPanel />
        </aside>
      </div>
    </PageShell>
  );
}
