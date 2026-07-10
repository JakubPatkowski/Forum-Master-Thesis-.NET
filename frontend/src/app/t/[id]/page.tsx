"use client";

/**
 * Thread detail (design: Thread.dc.html): breadcrumbs, the article (sanitized markdown
 * with inline media), download-style attachment rail (files attached to the thread but
 * not referenced inline), tags, reaction + owner/moderator actions, the comment tree,
 * and the three side panels (author card with real stats, ABOUT THREAD, ON THIS PAGE
 * from the markdown headings, RELATED THREADS via tag search).
 *
 * Realtime: subscribes to view=thread; updates re-fetch via invalidation, a delete
 * surfaces as a banner + the 404 state on re-fetch (notifications carry no content).
 */

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { CommentSection } from "@/components/comments/CommentSection";
import { useCompose } from "@/components/compose/compose-context";
import { ReactionButton } from "@/components/engagement/ReactionButton";
import { PageShell } from "@/components/layout/PageShell";
import { MarkdownView } from "@/components/markdown/MarkdownView";
import { Avatar } from "@/components/ui/Avatar";
import { Badge } from "@/components/ui/Badge";
import { CategoryIcon } from "@/components/ui/CategoryIcon";
import { ThreadIcon } from "@/components/ui/ThreadIcon";
import { ApiErrorState } from "@/components/ui/ErrorState";
import { LiveDot } from "@/components/ui/LiveDot";
import { Panel } from "@/components/ui/Panel";
import { Skeleton } from "@/components/ui/Skeleton";
import { TagChip } from "@/components/ui/TagChip";
import { useToast } from "@/components/ui/toast";
import { filesApi } from "@/lib/api/files";
import { queryKeys } from "@/lib/api/keys";
import { ApiError } from "@/lib/api/problem";
import { useAuth } from "@/lib/auth/auth-context";
import { useComments, useDeleteThread, useSearchThreads, useThread } from "@/lib/hooks/use-content";
import { useReactionBatch } from "@/lib/hooks/use-reactions";
import { useUserStats } from "@/lib/hooks/use-user-stats";
import { extractHeadings } from "@/lib/markdown/headings";
import { useRealtime, useRealtimeSubscription } from "@/lib/realtime/realtime-context";
import { formatBytes, timeAgoLabel } from "@/lib/utils/time";

import panelStyles from "@/components/panels/panels.module.css";
import styles from "./thread.module.css";

export default function ThreadPage() {
  const params = useParams<{ id: string }>();
  const threadId = params.id;
  const router = useRouter();
  const { currentUser, isModerator, isAuthenticated } = useAuth();
  const { openEdit } = useCompose();
  const { addNotificationListener } = useRealtime();
  const { showError, show } = useToast();

  const thread = useThread(threadId);
  const deleteThread = useDeleteThread();
  const [deletedLive, setDeletedLive] = useState(false);

  useRealtimeSubscription("thread", isAuthenticated ? threadId : null);

  useEffect(
    () =>
      addNotificationListener((notification) => {
        if (
          notification.entity === "thread" &&
          notification.type === "deleted" &&
          notification.id === threadId
        ) {
          setDeletedLive(true);
        }
      }),
    [addNotificationListener, threadId],
  );

  const stats = useUserStats(thread.data?.ownerId);

  const attachments = useQuery({
    queryKey: queryKeys.filesByTarget("thread", threadId),
    queryFn: () => filesApi.listByTarget("thread", threadId),
    enabled: thread.data !== undefined,
    staleTime: 60_000,
  });

  // Files referenced inline via the media convention render inside the body — the
  // attachment rail lists only the rest, as downloadable extras.
  const inlineRefs = useMemo(() => {
    const body = thread.data?.body ?? "";
    return new Set(
      [...body.matchAll(/\(image:([0-9A-HJKMNP-TV-Z]{26})\)/gi)].map((m) => m[1]!.toUpperCase()),
    );
  }, [thread.data?.body]);
  const extraAttachments = (attachments.data ?? []).filter(
    (file) => !inlineRefs.has(file.fileId.toUpperCase()),
  );

  const headings = useMemo(
    () => (thread.data ? extractHeadings(thread.data.body) : []),
    [thread.data],
  );

  const firstTag = thread.data?.tags[0];
  const related = useSearchThreads(firstTag ?? "", 5);
  const relatedItems = (related.data?.pages[0]?.items ?? []).filter((t) => t.id !== threadId);

  // Same query keys as CommentSection's tree + batch hydration — React Query dedupes,
  // so this reads the cached data instead of issuing a second network call.
  const comments = useComments(threadId);
  const commentIds = useMemo(() => (comments.data ?? []).map((c) => c.id), [comments.data]);
  const commentReactions = useReactionBatch("comment", commentIds);
  const commentLikesTotal = useMemo(() => {
    if (commentIds.length === 0) return 0;
    if (!commentReactions.data) return null;
    let total = 0;
    for (const summary of commentReactions.data.values()) total += summary.count;
    return total;
  }, [commentReactions.data, commentIds.length]);

  if (thread.error instanceof ApiError) {
    return (
      <PageShell wide={false}>
        <ApiErrorState error={thread.error} onRetry={() => void thread.refetch()} />
      </PageShell>
    );
  }

  const detail = thread.data;
  const isOwner = detail !== undefined && currentUser?.id === detail.ownerId;
  const mayModify = isOwner || isModerator;

  const onDelete = () => {
    if (!detail) return;
    if (!window.confirm("Delete this thread? Comments stay readable but the thread 404s.")) return;
    deleteThread.mutate(detail.id, {
      onSuccess: () => {
        show("success", "Thread deleted");
        router.push(`/c/${detail.categorySlug}`);
      },
      onError: (error) => showError(error),
    });
  };

  return (
    <PageShell>
      <div className={styles.grid}>
        <aside className={styles.left}>
          {detail ? (
            <Panel>
              <div className={styles.authorCard}>
                <Avatar
                  userId={detail.ownerId}
                  displayName={detail.displayName}
                  size={72}
                  brackets
                />
                <div className={styles.authorNames}>
                  <Link href={`/u/${detail.ownerId}`} className={styles.authorName}>
                    {detail.displayName}
                  </Link>
                  <div className={styles.authorHandle}>@{detail.username}</div>
                </div>
                <Badge tone="accent">
                  <svg width="9" height="9" viewBox="0 0 24 24" fill="currentColor">
                    <path d="M12 2 4 6v6c0 5 3.4 8.5 8 10 4.6-1.5 8-5 8-10V6l-8-4z" />
                  </svg>
                  OP · THREAD AUTHOR
                </Badge>
              </div>
              <div className={styles.statsGrid}>
                <div className={styles.statCell}>
                  <div className={styles.statValue}>{stats.data?.threadCount ?? "–"}</div>
                  <div className={styles.statLabel}>THREADS</div>
                </div>
                <div className={styles.statCell}>
                  <div className={styles.statValue}>{stats.data?.commentCount ?? "–"}</div>
                  <div className={styles.statLabel}>COMMENTS</div>
                </div>
                <div className={styles.statCell}>
                  <div className={`${styles.statValue} ${styles.statAccent}`}>
                    {stats.data?.karma ?? "–"}
                  </div>
                  <div className={styles.statLabel}>KARMA</div>
                </div>
              </div>
              <div className={styles.authorActions}>
                <Link
                  href="/social"
                  className={styles.messageButton}
                  title="Messaging is a preview — backend not implemented"
                >
                  MESSAGE
                </Link>
              </div>
            </Panel>
          ) : (
            <Skeleton height={280} />
          )}
        </aside>

        <main className={styles.main}>
          <nav className={styles.breadcrumbs}>
            <Link href="/" className={styles.crumb}>
              FORUM
            </Link>
            <span className={styles.crumbSep}>/</span>
            {detail ? (
              <Link href={`/c/${detail.categorySlug}`} className={styles.crumb}>
                {detail.categoryName.toUpperCase()}
              </Link>
            ) : (
              <Skeleton width={120} height={11} />
            )}
            <span className={styles.crumbSep}>/</span>
            <span className={styles.crumbCurrent}>THREAD</span>
          </nav>

          {deletedLive ? (
            <div className={styles.deletedBanner} role="alert">
              This thread was just deleted by its owner or a moderator — it now returns 404.
            </div>
          ) : null}

          {detail ? (
            <article className={styles.article}>
              <div className={styles.articleHead}>
                <div className={styles.categoryRow}>
                  <ThreadIcon
                    threadId={detail.id}
                    categoryId={detail.categoryId}
                    categoryName={detail.categoryName}
                    categorySlug={detail.categorySlug}
                    size={44}
                  />
                  <div>
                    <Link href={`/c/${detail.categorySlug}`} className={styles.categoryLink}>
                      {detail.categoryName.toUpperCase()}
                    </Link>
                    <div className={styles.timeRow}>
                      {timeAgoLabel(detail.createdOnUtc)} AGO
                      {detail.lastModifiedOnUtc ? (
                        <Badge className={styles.editedBadge}>EDITED</Badge>
                      ) : null}
                      {detail.isPinned ? <Badge tone="accent">PINNED</Badge> : null}
                    </div>
                  </div>
                </div>
                <h1 className={styles.title}>{detail.title}</h1>
                <div className={styles.authorRow}>
                  <Avatar userId={detail.ownerId} displayName={detail.displayName} size={34} />
                  <div>
                    <div className={styles.authorRowName}>{detail.displayName}</div>
                    <div className={styles.authorRowHandle}>@{detail.username}</div>
                  </div>
                </div>
              </div>

              <MarkdownView markdown={detail.body} className={styles.body} />

              {extraAttachments.length > 0 ? (
                <div className={styles.attachments}>
                  <div className={styles.attachmentsLabel}>
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor">
                      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8l-6-6zm-3 5V3.5L18.5 9H13z" />
                    </svg>
                    <span>ATTACHMENTS</span>
                  </div>
                  <div className={styles.attachmentGrid}>
                    {extraAttachments.map((file) => (
                      <a
                        key={file.fileId}
                        href={file.url}
                        target="_blank"
                        rel="noopener noreferrer"
                        className={styles.attachmentRow}
                      >
                        {/* eslint-disable-next-line @next/next/no-img-element */}
                        <img src={file.url} alt="" className={styles.attachmentThumb} />
                        <span className={styles.attachmentInfo}>
                          <span className={styles.attachmentName}>
                            {file.contentType} · {file.width}×{file.height}
                          </span>
                          <span className={styles.attachmentSize}>
                            {formatBytes(file.sizeBytes)}
                          </span>
                        </span>
                      </a>
                    ))}
                  </div>
                </div>
              ) : null}

              {detail.tags.length > 0 ? (
                <div className={styles.tags}>
                  {detail.tags.map((tag) => (
                    <TagChip key={tag} slug={tag} />
                  ))}
                </div>
              ) : null}

              <div className={styles.articleFooter}>
                <ReactionButton targetType="thread" targetId={detail.id} />
                <span className={styles.footerSpacer} />
                {mayModify ? (
                  <div className={styles.ownerActions}>
                    <button className={styles.editButton} onClick={() => openEdit(detail)}>
                      EDIT
                    </button>
                    <button
                      className={styles.deleteButton}
                      onClick={onDelete}
                      disabled={deleteThread.isPending}
                    >
                      DELETE
                    </button>
                  </div>
                ) : null}
              </div>
            </article>
          ) : (
            <div className={styles.articleSkeleton}>
              <Skeleton height={44} width="60%" />
              <Skeleton height={16} width="30%" />
              <Skeleton height={220} />
            </div>
          )}

          {detail ? <CommentSection threadId={detail.id} threadOwnerId={detail.ownerId} /> : null}
        </main>

        <aside className={styles.right}>
          {detail ? (
            <Panel label="ABOUT THREAD">
              <div className={panelStyles.kvList}>
                <div className={panelStyles.kvRow}>
                  <span className={panelStyles.kvKey}>CREATED</span>
                  <span className={panelStyles.kvValue}>
                    {timeAgoLabel(detail.createdOnUtc)} AGO
                  </span>
                </div>
                <div className={panelStyles.kvRow}>
                  <span className={panelStyles.kvKey}>CATEGORY</span>
                  <Link className={panelStyles.kvLink} href={`/c/${detail.categorySlug}`}>
                    {detail.categoryName}
                  </Link>
                </div>
                <div className={panelStyles.kvRow}>
                  <span className={panelStyles.kvKey}>TAGS</span>
                  {detail.tags.length > 0 ? (
                    <span className={panelStyles.kvTags}>
                      {detail.tags.map((tag) => (
                        <TagChip key={tag} slug={tag} />
                      ))}
                    </span>
                  ) : (
                    <span className={panelStyles.kvValue}>none</span>
                  )}
                </div>
                <div className={panelStyles.kvRow}>
                  <span className={panelStyles.kvKey}>COMMENT LIKES</span>
                  <span className={panelStyles.kvValue}>{commentLikesTotal ?? "–"}</span>
                </div>
              </div>
            </Panel>
          ) : null}

          {headings.length > 0 ? (
            <Panel label="ON THIS PAGE" accent="cyan">
              <div className={panelStyles.tocList}>
                {headings.map((heading) => (
                  <a
                    key={heading.slug}
                    href={`#${heading.slug}`}
                    className={
                      heading.depth > 2
                        ? `${panelStyles.tocLink} ${panelStyles.tocNested}`
                        : panelStyles.tocLink
                    }
                  >
                    {heading.text}
                  </a>
                ))}
              </div>
            </Panel>
          ) : null}

          {firstTag && relatedItems.length > 0 ? (
            <Panel label="RELATED THREADS">
              <div className={styles.relatedList}>
                {relatedItems.slice(0, 4).map((item) => (
                  <Link key={item.id} href={`/t/${item.id}`} className={styles.relatedRow}>
                    <CategoryIcon
                      categoryId={item.categoryId}
                      name={item.categoryName}
                      seed={item.categorySlug}
                      size={28}
                    />
                    <span className={styles.relatedText}>
                      <span className={styles.relatedTitle}>{item.title}</span>
                      <span className={styles.relatedMeta}>@{item.username}</span>
                    </span>
                  </Link>
                ))}
              </div>
            </Panel>
          ) : null}

          {isAuthenticated ? (
            <Panel>
              <div className={panelStyles.subscribedNote}>
                <LiveDot color="cyan" size={7} />
                <span>SUBSCRIBED · view=thread</span>
              </div>
            </Panel>
          ) : null}
        </aside>
      </div>
    </PageShell>
  );
}
