"use client";

/**
 * Category page (design: Category.dc.html): header with visibility badge + slug,
 * MOST POPULAR callout (computed client-side from the loaded page's batch like counts —
 * no popularity endpoint exists), pinned-first keyset thread feed, live banner scoped
 * to this category, realtime subscription on the category view.
 */

import Link from "next/link";
import { useParams } from "next/navigation";
import { useMemo } from "react";

import { useCompose } from "@/components/compose/compose-context";
import { CategorySidebar } from "@/components/layout/CategorySidebar";
import { PageShell } from "@/components/layout/PageShell";
import { LiveActivityPanel } from "@/components/panels/LiveActivityPanel";
import { ThreadCard } from "@/components/thread/ThreadCard";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { ApiErrorState } from "@/components/ui/ErrorState";
import { LiveBanner } from "@/components/ui/LiveBanner";
import { LiveDot } from "@/components/ui/LiveDot";
import { LoadMoreButton } from "@/components/ui/LoadMoreButton";
import { Monogram } from "@/components/ui/Monogram";
import { Panel } from "@/components/ui/Panel";
import { Skeleton, ThreadCardSkeleton } from "@/components/ui/Skeleton";
import { useToast } from "@/components/ui/toast";
import { ApiError } from "@/lib/api/problem";
import { useAuth } from "@/lib/auth/auth-context";
import { useCategory, usePinThread, useThreadFeed } from "@/lib/hooks/use-content";
import { useNewThreadBanner } from "@/lib/hooks/use-new-thread-banner";
import { useReactionBatch } from "@/lib/hooks/use-reactions";
import { useRealtimeSubscription } from "@/lib/realtime/realtime-context";

import panelStyles from "@/components/panels/panels.module.css";
import styles from "./category.module.css";

export default function CategoryPage() {
  const params = useParams<{ slug: string }>();
  const slug = params.slug;
  const { isAuthenticated, currentUser, isModerator } = useAuth();
  const { openCreate } = useCompose();
  const { showError } = useToast();

  const category = useCategory(slug);
  const categoryId = category.data?.id;
  const feed = useThreadFeed(categoryId);
  const pinThread = usePinThread(categoryId ?? "");
  const banner = useNewThreadBanner(categoryId);

  useRealtimeSubscription("category", isAuthenticated ? categoryId : null);

  const threads = useMemo(() => feed.data?.pages.flatMap((page) => page.items) ?? [], [feed.data]);
  const threadIds = useMemo(() => threads.map((t) => t.id), [threads]);
  const reactions = useReactionBatch("thread", threadIds);

  const mostPopular = useMemo(() => {
    if (!reactions.data || threads.length === 0) return null;
    let best: { id: string; count: number } | null = null;
    for (const thread of threads) {
      const count = reactions.data.get(thread.id)?.count ?? 0;
      if (count > 0 && (!best || count > best.count)) best = { id: thread.id, count };
    }
    return best ? (threads.find((t) => t.id === best.id) ?? null) : null;
  }, [reactions.data, threads]);

  if (category.error instanceof ApiError) {
    return (
      <PageShell wide={false}>
        <ApiErrorState error={category.error} />
      </PageShell>
    );
  }

  const canEditCategory =
    category.data && (isModerator || currentUser?.id === category.data.ownerId);

  const togglePin = (threadId: string, pinned: boolean) => {
    pinThread.mutate({ threadId, pinned: !pinned }, { onError: (error) => showError(error) });
  };

  return (
    <PageShell>
      <div className={styles.grid}>
        <aside className={styles.left}>
          <CategorySidebar activeSlug={slug} />
        </aside>

        <main className={styles.main}>
          {category.isLoading ? (
            <Skeleton height={96} />
          ) : category.data ? (
            <header className={styles.header}>
              <Monogram name={category.data.name} seed={category.data.slug} size={56} />
              <div className={styles.headerText}>
                <div className={styles.headerTitleRow}>
                  <h1 className={styles.headerTitle}>{category.data.name}</h1>
                  <Badge tone={category.data.visibility === "private" ? "warning" : "neutral"}>
                    {category.data.visibility}
                  </Badge>
                  <span className={styles.slug}>/{category.data.slug}</span>
                </div>
                {category.data.description ? (
                  <p className={styles.description}>{category.data.description}</p>
                ) : null}
              </div>
              <div className={styles.headerActions}>
                {canEditCategory ? (
                  <span title="Category editing UI is a later increment — the API (PUT /api/content/categories/{slug}) is ready.">
                    <Badge>OWNER TOOLS SOON</Badge>
                  </span>
                ) : null}
                <Button onClick={() => openCreate(category.data.id)}>+ New thread</Button>
              </div>
            </header>
          ) : null}

          {banner.pendingCount > 0 ? (
            <LiveBanner
              message={
                banner.pendingCount === 1
                  ? "1 new thread arrived in this category"
                  : `${banner.pendingCount} new threads arrived in this category`
              }
              onAction={() => {
                banner.clear();
                void feed.refetch();
              }}
            />
          ) : null}

          {mostPopular ? (
            <section>
              <div className={styles.sectionLabel}>
                <svg width="13" height="13" viewBox="0 0 24 24" fill="var(--color-accent-base)">
                  <path d="M12 21s-8-5.5-8-11a4.5 4.5 0 0 1 8-2.8A4.5 4.5 0 0 1 20 10c0 5.5-8 11-8 11z" />
                </svg>
                <span>MOST POPULAR</span>
              </div>
              <div className={styles.popular}>
                <ThreadCard
                  thread={mostPopular}
                  reaction={reactions.data?.get(mostPopular.id)}
                  showCategory={false}
                />
              </div>
            </section>
          ) : null}

          <section>
            <div className={styles.feedHeader}>
              <h2 className={styles.feedTitle}>Threads</h2>
              <div className={styles.feedNote}>PINNED FIRST · CURSOR PAGED</div>
            </div>

            {feed.error instanceof ApiError ? (
              <ApiErrorState error={feed.error} onRetry={() => void feed.refetch()} />
            ) : feed.isLoading || !categoryId ? (
              <div className={styles.list}>
                <ThreadCardSkeleton />
                <ThreadCardSkeleton />
                <ThreadCardSkeleton />
              </div>
            ) : threads.length === 0 ? (
              <EmptyState
                title="No threads here yet"
                description={`Be the first to post in ${category.data?.name ?? "this category"}.`}
                action={
                  isAuthenticated ? (
                    <Button onClick={() => openCreate(categoryId)}>Start a thread</Button>
                  ) : undefined
                }
              />
            ) : (
              <>
                <div className={styles.list}>
                  {threads.map((thread) => (
                    <ThreadCard
                      key={thread.id}
                      thread={thread}
                      reaction={reactions.data?.get(thread.id)}
                      showCategory={false}
                      pinAction={
                        isModerator
                          ? {
                              pinned: thread.isPinned,
                              onToggle: () => togglePin(thread.id, thread.isPinned),
                            }
                          : undefined
                      }
                    />
                  ))}
                </div>
                <LoadMoreButton
                  onClick={() => void feed.fetchNextPage()}
                  loading={feed.isFetchingNextPage}
                  hasMore={feed.hasNextPage}
                />
              </>
            )}
          </section>
        </main>

        <aside className={styles.right}>
          {category.data ? (
            <Panel label="ABOUT CATEGORY">
              <div className={panelStyles.kvList}>
                <div className={panelStyles.kvRow}>
                  <span className={panelStyles.kvKey}>SLUG</span>
                  <span className={panelStyles.kvValue}>{category.data.slug}</span>
                </div>
                <div className={panelStyles.kvRow}>
                  <span className={panelStyles.kvKey}>VISIBILITY</span>
                  <span className={panelStyles.kvValue}>{category.data.visibility}</span>
                </div>
                <div className={panelStyles.kvRow}>
                  <span className={panelStyles.kvKey}>OWNER</span>
                  <Link className={panelStyles.kvLink} href={`/u/${category.data.ownerId}`}>
                    profile →
                  </Link>
                </div>
                <div className={panelStyles.kvRow}>
                  <span className={panelStyles.kvKey}>CREATED</span>
                  <span className={panelStyles.kvValue}>
                    {category.data.createdOnUtc.slice(0, 10)}
                  </span>
                </div>
              </div>
            </Panel>
          ) : null}
          {isAuthenticated ? (
            <Panel>
              <div className={panelStyles.subscribedNote}>
                <LiveDot color="cyan" size={7} />
                <span>SUBSCRIBED · view=category</span>
              </div>
            </Panel>
          ) : null}
          <LiveActivityPanel />
        </aside>
      </div>
    </PageShell>
  );
}
