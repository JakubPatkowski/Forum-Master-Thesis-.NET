"use client";

/**
 * Profile — own & others (design: Profile.dc.html). Stats come from the real
 * GET /api/engagement/users/{id}/stats (a zero-content user is a 200 with zeros; only a
 * nonexistent id 404s). Own profile adds CHANGE AVATAR (real Files flow with
 * targetType=avatar replace semantics) and LOG OUT ALL DEVICES. Recent activity merges
 * the owner's thread + comment keyset feeds client-side (lib/feed/activity-merge.ts —
 * the same "never splice older above newer" invariant as the home feed's k-way merge).
 */

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useMemo, useRef, useState, type ChangeEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";

import { PageShell } from "@/components/layout/PageShell";
import { Avatar } from "@/components/ui/Avatar";
import { Badge } from "@/components/ui/Badge";
import { EmptyState } from "@/components/ui/EmptyState";
import { ApiErrorState } from "@/components/ui/ErrorState";
import { LoadMoreButton } from "@/components/ui/LoadMoreButton";
import { Panel } from "@/components/ui/Panel";
import { Skeleton } from "@/components/ui/Skeleton";
import { useToast } from "@/components/ui/toast";
import { filesApi } from "@/lib/api/files";
import { queryKeys } from "@/lib/api/keys";
import { ApiError } from "@/lib/api/problem";
import {
  ALLOWED_UPLOAD_TYPES,
  type CommentActivityItemResponse,
  type ThreadFeedItemResponse,
} from "@/lib/api/types";
import { useAuth } from "@/lib/auth/auth-context";
import { mergeActivity } from "@/lib/feed/activity-merge";
import { useUserComments, useUserThreads } from "@/lib/hooks/use-content";
import { useUserStats } from "@/lib/hooks/use-user-stats";
import { useRealtimeSubscription } from "@/lib/realtime/realtime-context";
import { uploadFile } from "@/lib/upload/upload";
import { timeAgoLabel } from "@/lib/utils/time";

import styles from "./profile.module.css";

type ActivityRow =
  | { kind: "thread"; id: string; createdOnUtc: string; thread: ThreadFeedItemResponse }
  | { kind: "comment"; id: string; createdOnUtc: string; comment: CommentActivityItemResponse };

function ActivityTimeline({ userId }: { userId: string }) {
  const threads = useUserThreads(userId);
  const comments = useUserComments(userId);

  const merged = useMemo(() => {
    const threadRows: ActivityRow[] = (threads.data?.pages.flatMap((p) => p.items) ?? []).map(
      (t) => ({ kind: "thread", id: t.id, createdOnUtc: t.createdOnUtc, thread: t }),
    );
    const commentRows: ActivityRow[] = (comments.data?.pages.flatMap((p) => p.items) ?? []).map(
      (c) => ({ kind: "comment", id: c.id, createdOnUtc: c.createdOnUtc, comment: c }),
    );
    return mergeActivity<ActivityRow>([
      { items: threadRows, hasMore: threads.hasNextPage ?? false },
      { items: commentRows, hasMore: comments.hasNextPage ?? false },
    ]);
  }, [threads.data, comments.data, threads.hasNextPage, comments.hasNextPage]);

  if (threads.isLoading || comments.isLoading) {
    return (
      <div className={styles.activityList}>
        <Skeleton height={56} />
        <Skeleton height={56} />
        <Skeleton height={56} />
      </div>
    );
  }

  if (threads.error instanceof ApiError || comments.error instanceof ApiError) {
    return (
      <EmptyState
        title="Couldn't load activity"
        description="One of the activity feeds failed — try again."
      />
    );
  }

  if (merged.visible.length === 0 && merged.heldBack === 0) {
    return (
      <EmptyState
        title="Nothing here yet"
        description="Threads and comments show up here as this user posts."
      />
    );
  }

  // Fetching the next page of every refillable source keeps the merge frontier moving —
  // with two sources that's at most two requests per click.
  const loadMore = () => {
    if (threads.hasNextPage) void threads.fetchNextPage();
    if (comments.hasNextPage) void comments.fetchNextPage();
  };

  return (
    <>
      <div className={styles.activityList}>
        {merged.visible.map((row) =>
          row.kind === "thread" ? (
            <Link key={`t-${row.id}`} href={`/t/${row.id}`} className={styles.activityRow}>
              <Badge tone="accent">THREAD</Badge>
              <span className={styles.activityBody}>
                <span className={styles.activityTitle}>{row.thread.title}</span>
                <span className={styles.activityMeta}>
                  in {row.thread.categoryName} · {timeAgoLabel(row.createdOnUtc)}
                </span>
              </span>
            </Link>
          ) : (
            <Link
              key={`c-${row.id}`}
              href={`/t/${row.comment.threadId}#comment-${row.id}`}
              className={styles.activityRow}
            >
              <Badge tone="cyan">COMMENT</Badge>
              <span className={styles.activityBody}>
                <span className={styles.activityTitle}>on {row.comment.threadTitle}</span>
                <span className={styles.activityExcerpt}>
                  {row.comment.body.length > 140
                    ? `${row.comment.body.slice(0, 140)}…`
                    : row.comment.body}
                </span>
                <span className={styles.activityMeta}>{timeAgoLabel(row.createdOnUtc)}</span>
              </span>
            </Link>
          ),
        )}
      </div>
      <LoadMoreButton
        onClick={loadMore}
        loading={threads.isFetchingNextPage || comments.isFetchingNextPage}
        hasMore={(threads.hasNextPage ?? false) || (comments.hasNextPage ?? false)}
      />
    </>
  );
}

export default function ProfilePage() {
  const params = useParams<{ userId: string }>();
  const userId = params.userId;
  const router = useRouter();
  const queryClient = useQueryClient();
  const { currentUser, logoutAll, isAuthenticated } = useAuth();
  const { showError, show } = useToast();
  const stats = useUserStats(userId);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [avatarBusy, setAvatarBusy] = useState(false);

  const isOwn = currentUser?.id === userId;

  // Own profile subscribes to the self user view — multi-device like-state sync pushes
  // land here (reaction events are mirrored to the actor's own view).
  useRealtimeSubscription("user", isAuthenticated && isOwn ? userId : null);

  const onPickAvatar = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    setAvatarBusy(true);
    try {
      const committed = await uploadFile(file, () => {});
      // targetType=avatar has replace semantics — attaching the new one detaches the old.
      await filesApi.attach(committed.fileId, { targetType: "avatar", targetId: userId });
      await queryClient.invalidateQueries({ queryKey: queryKeys.filesByTarget("avatar", userId) });
      show("success", "Avatar updated");
    } catch (error) {
      showError(error);
    } finally {
      setAvatarBusy(false);
    }
  };

  if (stats.error instanceof ApiError) {
    return (
      <PageShell wide={false}>
        <ApiErrorState error={stats.error} onRetry={() => void stats.refetch()} />
      </PageShell>
    );
  }

  return (
    <PageShell>
      <div className={styles.grid}>
        <aside className={styles.left}>
          <Panel>
            <div className={styles.identity}>
              <Avatar
                userId={userId}
                displayName={stats.data?.displayName ?? "?"}
                size={96}
                brackets
              />
              {stats.data ? (
                <div className={styles.names}>
                  <div className={styles.displayName}>{stats.data.displayName}</div>
                  <div className={styles.username}>@{stats.data.username}</div>
                </div>
              ) : (
                <Skeleton width={140} height={20} />
              )}
              {isOwn ? (
                <button
                  className={styles.changeAvatar}
                  onClick={() => fileInputRef.current?.click()}
                  disabled={avatarBusy}
                >
                  {avatarBusy ? "UPLOADING…" : "CHANGE AVATAR"}
                </button>
              ) : null}
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

            {isOwn ? (
              <div className={styles.dangerZone}>
                <button
                  className={styles.logoutAll}
                  onClick={() => void logoutAll().then(() => router.push("/"))}
                >
                  LOG OUT ALL DEVICES
                </button>
                <span className={styles.dangerNote}>revokes every refresh token</span>
              </div>
            ) : null}
          </Panel>
        </aside>

        <main className={styles.main}>
          <Panel label="RECENT ACTIVITY">
            <ActivityTimeline userId={userId} />
          </Panel>
        </main>
      </div>

      <input
        ref={fileInputRef}
        type="file"
        accept={ALLOWED_UPLOAD_TYPES.join(",")}
        className={styles.hidden}
        onChange={(e) => void onPickAvatar(e)}
        aria-hidden
        tabIndex={-1}
      />
    </PageShell>
  );
}
