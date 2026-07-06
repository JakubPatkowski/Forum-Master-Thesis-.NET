"use client";

/**
 * Profile — own & others (design: Profile.dc.html). Stats come from the real
 * GET /api/engagement/users/{id}/stats (a zero-content user is a 200 with zeros; only a
 * nonexistent id 404s). Own profile adds CHANGE AVATAR (real Files flow with
 * targetType=avatar replace semantics) and LOG OUT ALL DEVICES. Recent activity is a
 * SOON mock — no feed-by-owner endpoint exists yet.
 */

import { useParams, useRouter } from "next/navigation";
import { useRef, useState, type ChangeEvent } from "react";
import { useQueryClient } from "@tanstack/react-query";

import { PageShell } from "@/components/layout/PageShell";
import { Avatar } from "@/components/ui/Avatar";
import { Badge } from "@/components/ui/Badge";
import { EmptyState } from "@/components/ui/EmptyState";
import { ApiErrorState } from "@/components/ui/ErrorState";
import { Panel } from "@/components/ui/Panel";
import { Skeleton } from "@/components/ui/Skeleton";
import { useToast } from "@/components/ui/toast";
import { filesApi } from "@/lib/api/files";
import { queryKeys } from "@/lib/api/keys";
import { ApiError } from "@/lib/api/problem";
import { ALLOWED_UPLOAD_TYPES } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/auth-context";
import { useUserStats } from "@/lib/hooks/use-user-stats";
import { useRealtimeSubscription } from "@/lib/realtime/realtime-context";
import { uploadFile } from "@/lib/upload/upload";

import styles from "./profile.module.css";

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
          <Panel
            label="RECENT ACTIVITY"
            headerExtra={
              <Badge
                tone="warning"
                title="Requires a feed filter by ownerId — not in the API yet; nothing to show"
              >
                SOON
              </Badge>
            }
          >
            <EmptyState
              title="Activity feed isn't wired yet"
              description="Listing a user's threads and comments needs a feed-by-owner API filter that doesn't exist yet. The stat counters on the left are live."
            />
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
