"use client";

/**
 * Social / Community — the real thing (backend: Forum.Modules.Social, /api/social/*).
 * Layout follows Social.dc.html: the FRIENDS/REQUESTS/GROUPS/IGNORED rail on the left,
 * and a detail column (messages + notifications + privacy, or a selected group) on the
 * right; chats float in the global dock (components/social/ChatDock), not in-page.
 *
 * Realtime: friends/requests/invites/notifications arrive via the app-wide own-user
 * view; the selected group subscribes its group view (inside GroupDetailPanel); open
 * chats subscribe their conversation views (inside ChatWindow). Presence is polled —
 * ONE batched request over the rows this page currently shows.
 *
 * Deep links: ?tab= selects a rail tab, ?group= opens a group's detail,
 * ?conversation= opens that chat in the dock (resolved from the conversation list).
 */

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useMemo, useRef, useState } from "react";

import { PageShell } from "@/components/layout/PageShell";
import { BlockedUserRow } from "@/components/social/BlockedUserRow";
import { ConversationRow } from "@/components/social/ConversationRow";
import { FriendRequestCard } from "@/components/social/FriendRequestCard";
import { FriendRow } from "@/components/social/FriendRow";
import { GroupCard } from "@/components/social/GroupCard";
import { GroupDetailPanel } from "@/components/social/GroupDetailPanel";
import { GroupInviteCard } from "@/components/social/GroupInviteCard";
import { GroupModal } from "@/components/social/GroupModal";
import { NotificationRow } from "@/components/social/NotificationRow";
import { SocialRowSkeleton } from "@/components/social/SocialRowSkeleton";
import { useChatDock } from "@/components/social/chat-dock-context";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { LiveDot } from "@/components/ui/LiveDot";
import { LoadMoreButton } from "@/components/ui/LoadMoreButton";
import { Panel } from "@/components/ui/Panel";
import { useToast } from "@/components/ui/toast";
import type { GroupListFilter, PrivacyAudience, PrivacySettingsResponse } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/auth-context";
import { usePresence } from "@/lib/hooks/use-presence";
import {
  useAcceptFriendRequest,
  useAcceptGroupInvite,
  useBlockedUsers,
  useBlockUser,
  useConversations,
  useDeclineGroupInvite,
  useDeleteFriendRequest,
  useFriendRequests,
  useFriends,
  useGroups,
  useMarkNotificationsRead,
  useMyInvites,
  useNotifications,
  usePrivacySettings,
  useRemoveFriend,
  useUnblockUser,
  useUnreadNotificationCount,
  useUpdatePrivacySettings,
} from "@/lib/hooks/use-social";
import { sortConversations } from "@/lib/social/conversations";
import { statusOf } from "@/lib/social/presence";

import styles from "./social.module.css";

type RailTab = "friends" | "requests" | "groups" | "ignored";

const RAIL_TABS: readonly RailTab[] = ["friends", "requests", "groups", "ignored"];

function SocialPageInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { isAuthenticated, isRestoring } = useAuth();
  const { openConversation, openGroupChat, openDirectChat } = useChatDock();

  const paramTab = searchParams.get("tab");
  const [tab, setTab] = useState<RailTab>(
    RAIL_TABS.includes(paramTab as RailTab) ? (paramTab as RailTab) : "friends",
  );
  const [selectedGroupId, setSelectedGroupId] = useState<string | null>(
    searchParams.get("group"),
  );

  const selectTab = (next: RailTab) => {
    setTab(next);
    router.replace(next === "friends" ? "/social" : `/social?tab=${next}`, { scroll: false });
  };
  const selectGroup = (groupId: string | null) => {
    setSelectedGroupId(groupId);
    if (groupId) router.replace(`/social?tab=groups&group=${groupId}`, { scroll: false });
    else router.replace(tab === "friends" ? "/social" : `/social?tab=${tab}`, { scroll: false });
  };

  // ?conversation= deep link (activity log / bell): open that chat once the
  // conversation list can resolve it. Runs once per param value.
  const conversations = useConversations();
  const conversationParam = searchParams.get("conversation");
  const openedParamRef = useRef<string | null>(null);
  useEffect(() => {
    if (!conversationParam || openedParamRef.current === conversationParam) return;
    const row = conversations.data?.find((c) => c.conversationId === conversationParam);
    if (row) {
      openedParamRef.current = conversationParam;
      openConversation(row);
    }
  }, [conversationParam, conversations.data, openConversation]);

  const requests = useFriendRequests();
  const invites = useMyInvites();
  const incomingCount = requests.data?.incoming.length ?? 0;
  const inviteCount = invites.data?.length ?? 0;

  if (!isRestoring && !isAuthenticated) {
    return (
      <PageShell>
        <EmptyState
          title="Social is for signed-in users"
          description="Friends, groups and messages all live behind your account."
          action={
            <Link href="/auth">
              <Button>Log in</Button>
            </Link>
          }
        />
      </PageShell>
    );
  }

  return (
    <PageShell>
      <div className={styles.grid}>
        <section className={styles.rail}>
          <RailHeader />
          <div className={styles.tabs}>
            <button
              className={tab === "friends" ? styles.tabActive : styles.tab}
              onClick={() => selectTab("friends")}
            >
              Friends
            </button>
            <button
              className={tab === "requests" ? styles.tabActive : styles.tab}
              onClick={() => selectTab("requests")}
            >
              Requests
              {incomingCount > 0 ? <span className={styles.tabBadge}>{incomingCount}</span> : null}
            </button>
            <button
              className={tab === "groups" ? styles.tabActive : styles.tab}
              onClick={() => selectTab("groups")}
            >
              Groups
              {inviteCount > 0 ? <span className={styles.tabBadge}>{inviteCount}</span> : null}
            </button>
            <button
              className={tab === "ignored" ? styles.tabActive : styles.tab}
              onClick={() => selectTab("ignored")}
            >
              Ignored
            </button>
          </div>
          <div className={`${styles.railBody} panel-scroll`}>
            {tab === "friends" ? (
              <FriendsTab onMessage={(userId, username) => void openDirectChat(userId, username)} />
            ) : null}
            {tab === "requests" ? <RequestsTab /> : null}
            {tab === "groups" ? (
              <GroupsTab selectedGroupId={selectedGroupId} onSelect={selectGroup} />
            ) : null}
            {tab === "ignored" ? <IgnoredTab /> : null}
          </div>
        </section>

        <div className={styles.main}>
          {selectedGroupId ? (
            <GroupDetailPanel
              groupId={selectedGroupId}
              onOpenChat={openGroupChat}
              onMessageUser={(userId, username) => void openDirectChat(userId, username)}
              onClose={() => selectGroup(null)}
            />
          ) : (
            <>
              <MessagesPanel />
              <NotificationsPanel />
              <PrivacyPanel />
            </>
          )}
        </div>
      </div>
    </PageShell>
  );
}

function RailHeader() {
  const friends = useFriends();
  const friendRows = friends.data?.pages.flatMap((p) => p.items) ?? [];
  const presence = usePresence(friendRows.map((f) => f.userId));
  const onlineCount = friendRows.filter(
    (f) => statusOf(presence.data ?? new Map(), f.userId) !== "offline",
  ).length;

  return (
    <div className={styles.railHeader}>
      <LiveDot color="green" size={8} />
      <span className={styles.railTitle}>SOCIAL</span>
      <span className={styles.railCount}>{onlineCount} online</span>
    </div>
  );
}

function FriendsTab({ onMessage }: { onMessage: (userId: string, username: string) => void }) {
  const { showError } = useToast();
  const friends = useFriends();
  const removeFriend = useRemoveFriend();
  const blockUser = useBlockUser();
  const [filter, setFilter] = useState("");

  const rows = useMemo(
    () => friends.data?.pages.flatMap((p) => p.items) ?? [],
    [friends.data],
  );
  const presence = usePresence(rows.map((f) => f.userId));
  const map = presence.data ?? new Map();

  const visible = filter.trim()
    ? rows.filter((f) => f.username.toLowerCase().includes(filter.trim().toLowerCase()))
    : rows;
  const online = visible.filter((f) => statusOf(map, f.userId) !== "offline");
  const offline = visible.filter((f) => statusOf(map, f.userId) === "offline");

  const rowFor = (friend: (typeof rows)[number]) => (
    <FriendRow
      key={friend.friendshipId}
      userId={friend.userId}
      username={friend.username}
      status={statusOf(map, friend.userId)}
      onMessage={() => onMessage(friend.userId, friend.username)}
      onRemove={() => {
        if (!window.confirm(`Remove @${friend.username} from your friends?`)) return;
        removeFriend.mutate(friend.userId, {
          onError: (error) => showError(error, "Couldn't remove the friend."),
        });
      }}
      onBlock={() => {
        if (
          !window.confirm(
            `Block @${friend.username}? This removes the friendship and they won't be able to message you.`,
          )
        )
          return;
        blockUser.mutate(friend.userId, {
          onError: (error) => showError(error, "Couldn't block the user."),
        });
      }}
    />
  );

  return (
    <>
      <input
        className={styles.filterInput}
        placeholder="Find a friend…"
        value={filter}
        onChange={(e) => setFilter(e.target.value)}
        aria-label="Filter friends"
      />
      {friends.isLoading ? (
        <>
          <SocialRowSkeleton />
          <SocialRowSkeleton />
          <SocialRowSkeleton />
        </>
      ) : rows.length === 0 ? (
        <div className={styles.railEmpty}>
          No friends yet — send a request from someone&apos;s profile.
        </div>
      ) : (
        <>
          <div className={styles.groupLabel}>ONLINE · {online.length}</div>
          {online.map(rowFor)}
          <div className={styles.groupLabel}>OFFLINE · {offline.length}</div>
          {offline.map(rowFor)}
          <LoadMoreButton
            onClick={() => void friends.fetchNextPage()}
            loading={friends.isFetchingNextPage}
            hasMore={friends.hasNextPage ?? false}
          />
        </>
      )}
    </>
  );
}

function RequestsTab() {
  const { showError } = useToast();
  const requests = useFriendRequests();
  const accept = useAcceptFriendRequest();
  const remove = useDeleteFriendRequest();

  const incoming = requests.data?.incoming ?? [];
  const outgoing = requests.data?.outgoing ?? [];

  return (
    <>
      <div className={styles.groupLabel}>INCOMING</div>
      {requests.isLoading ? (
        <SocialRowSkeleton />
      ) : incoming.length === 0 ? (
        <div className={styles.railEmpty}>No pending requests.</div>
      ) : (
        incoming.map((request) => (
          <div key={request.friendshipId} className={styles.cardSlot}>
            <FriendRequestCard
              request={request}
              direction="incoming"
              busy={accept.isPending || remove.isPending}
              onAccept={() =>
                accept.mutate(request.friendshipId, {
                  onError: (error) => showError(error, "Couldn't accept the request."),
                })
              }
              onDecline={() =>
                remove.mutate(request.friendshipId, {
                  onError: (error) => showError(error, "Couldn't decline the request."),
                })
              }
            />
          </div>
        ))
      )}
      <div className={styles.groupLabel}>SENT</div>
      {outgoing.length === 0 ? (
        <div className={styles.railEmpty}>Nothing waiting on the other side.</div>
      ) : (
        outgoing.map((request) => (
          <div key={request.friendshipId} className={styles.cardSlot}>
            <FriendRequestCard
              request={request}
              direction="outgoing"
              busy={remove.isPending}
              onDecline={() =>
                remove.mutate(request.friendshipId, {
                  onError: (error) => showError(error, "Couldn't cancel the request."),
                })
              }
            />
          </div>
        ))
      )}
    </>
  );
}

function GroupsTab({
  selectedGroupId,
  onSelect,
}: {
  selectedGroupId: string | null;
  onSelect: (groupId: string) => void;
}) {
  const { showError } = useToast();
  const [filter, setFilter] = useState<GroupListFilter>("mine");
  const [createOpen, setCreateOpen] = useState(false);
  const groups = useGroups(filter);
  const invites = useMyInvites();
  const acceptInvite = useAcceptGroupInvite();
  const declineInvite = useDeclineGroupInvite();

  const rows = groups.data?.pages.flatMap((p) => p.items) ?? [];
  const inviteRows = invites.data ?? [];

  return (
    <>
      <button className={styles.newGroupButton} onClick={() => setCreateOpen(true)}>
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <path d="M12 5v14M5 12h14" />
        </svg>
        <span>NEW GROUP</span>
      </button>

      {inviteRows.length > 0 ? (
        <>
          <div className={styles.groupLabel}>INVITES · {inviteRows.length}</div>
          {inviteRows.map((invite) => (
            <div key={invite.inviteId} className={styles.cardSlot}>
              <GroupInviteCard
                invite={invite}
                busy={acceptInvite.isPending || declineInvite.isPending}
                onAccept={() =>
                  acceptInvite.mutate(invite.inviteId, {
                    onSuccess: () => onSelect(invite.groupId),
                    onError: (error) => showError(error, "Couldn't accept the invite."),
                  })
                }
                onDecline={() =>
                  declineInvite.mutate(invite.inviteId, {
                    onError: (error) => showError(error, "Couldn't decline the invite."),
                  })
                }
              />
            </div>
          ))}
        </>
      ) : null}

      <div className={styles.segmented}>
        <button
          className={filter === "mine" ? styles.segmentActive : styles.segment}
          onClick={() => setFilter("mine")}
        >
          MINE
        </button>
        <button
          className={filter === "public" ? styles.segmentActive : styles.segment}
          onClick={() => setFilter("public")}
        >
          DISCOVER
        </button>
      </div>

      {groups.isLoading ? (
        <>
          <SocialRowSkeleton />
          <SocialRowSkeleton />
        </>
      ) : rows.length === 0 ? (
        <div className={styles.railEmpty}>
          {filter === "mine"
            ? "You're not in any group yet — create one or discover public groups."
            : "No public groups to discover yet."}
        </div>
      ) : (
        rows.map((group) => (
          <GroupCard
            key={group.groupId}
            group={group}
            selected={group.groupId === selectedGroupId}
            onOpen={() => onSelect(group.groupId)}
          />
        ))
      )}
      <LoadMoreButton
        onClick={() => void groups.fetchNextPage()}
        loading={groups.isFetchingNextPage}
        hasMore={groups.hasNextPage ?? false}
      />

      {createOpen ? (
        <GroupModal
          mode="create"
          onClose={() => setCreateOpen(false)}
          onCreated={(groupId) => onSelect(groupId)}
        />
      ) : null}
    </>
  );
}

function IgnoredTab() {
  const { showError } = useToast();
  const blocked = useBlockedUsers();
  const unblock = useUnblockUser();
  const rows = blocked.data ?? [];

  return (
    <>
      <p className={styles.ignoredNote}>
        Ignored users can&apos;t message you or send you requests — to them it just looks like
        nothing happened.
      </p>
      {blocked.isLoading ? (
        <SocialRowSkeleton />
      ) : rows.length === 0 ? (
        <div className={styles.railEmpty}>You haven&apos;t ignored anyone.</div>
      ) : (
        rows.map((user) => (
          <BlockedUserRow
            key={user.userId}
            user={user}
            busy={unblock.isPending}
            onUnblock={() =>
              unblock.mutate(user.userId, {
                onError: (error) => showError(error, "Couldn't unblock the user."),
              })
            }
          />
        ))
      )}
    </>
  );
}

function MessagesPanel() {
  const { openConversation } = useChatDock();
  const conversations = useConversations();
  const rows = useMemo(() => sortConversations(conversations.data ?? []), [conversations.data]);
  const presence = usePresence(
    rows.filter((c) => c.type === "direct" && c.otherUserId).map((c) => c.otherUserId!),
  );

  return (
    <Panel label="MESSAGES" accent="cyan">
      <div className={styles.panelBody}>
        {conversations.isLoading ? (
          <>
            <SocialRowSkeleton />
            <SocialRowSkeleton />
          </>
        ) : rows.length === 0 ? (
          <div className={styles.railEmpty}>
            No conversations yet — message a friend from the rail.
          </div>
        ) : (
          rows.map((conversation) => (
            <ConversationRow
              key={conversation.conversationId}
              conversation={conversation}
              status={
                conversation.otherUserId
                  ? statusOf(presence.data ?? new Map(), conversation.otherUserId)
                  : undefined
              }
              onOpen={() => openConversation(conversation)}
            />
          ))
        )}
      </div>
    </Panel>
  );
}

function NotificationsPanel() {
  const { showError } = useToast();
  const notifications = useNotifications();
  const unread = useUnreadNotificationCount();
  const markRead = useMarkNotificationsRead();
  const rows = notifications.data?.pages.flatMap((p) => p.items) ?? [];
  const unreadCount = unread.data?.unread ?? 0;

  return (
    <Panel
      label="NOTIFICATIONS"
      headerExtra={
        unreadCount > 0 ? (
          <span className={styles.panelHeaderExtra}>
            <Badge tone="accent">{unreadCount} NEW</Badge>
            <button
              className={styles.markAllRead}
              onClick={() =>
                markRead.mutate(undefined, {
                  onError: (error) => showError(error, "Couldn't mark notifications read."),
                })
              }
            >
              MARK ALL READ
            </button>
          </span>
        ) : undefined
      }
    >
      <div className={styles.panelBody}>
        {notifications.isLoading ? (
          <SocialRowSkeleton />
        ) : rows.length === 0 ? (
          <div className={styles.railEmpty}>Nothing yet — friend and group activity lands here.</div>
        ) : (
          rows.map((notification) => (
            <NotificationRow key={notification.notificationId} notification={notification} />
          ))
        )}
        <LoadMoreButton
          onClick={() => void notifications.fetchNextPage()}
          loading={notifications.isFetchingNextPage}
          hasMore={notifications.hasNextPage ?? false}
        />
      </div>
    </Panel>
  );
}

const AUDIENCES: readonly PrivacyAudience[] = ["everyone", "friends", "no_one"];
// "friends" is meaningless for friend requests (friends need no request) — the backend
// normalizes it to no_one, so the UI never offers it there.
const FRIEND_REQUEST_AUDIENCES: readonly PrivacyAudience[] = ["everyone", "no_one"];
const AUDIENCE_LABEL: Record<PrivacyAudience, string> = {
  everyone: "everyone",
  friends: "friends only",
  no_one: "no one",
};

function PrivacyPanel() {
  const { showError, show } = useToast();
  const settings = usePrivacySettings();
  const update = useUpdatePrivacySettings();
  const [draft, setDraft] = useState<PrivacySettingsResponse | null>(null);

  // Local draft mirrors the server row once loaded; SAVE pushes the whole shape back.
  useEffect(() => {
    if (settings.data && draft === null) setDraft(settings.data);
  }, [settings.data, draft]);

  if (settings.isLoading || !draft) {
    return (
      <Panel label="PRIVACY">
        <div className={styles.panelBody}>
          <SocialRowSkeleton />
        </div>
      </Panel>
    );
  }

  const dirty =
    settings.data !== undefined &&
    (draft.friendRequests !== settings.data.friendRequests ||
      draft.messages !== settings.data.messages ||
      draft.groupInvites !== settings.data.groupInvites ||
      draft.showOnlineStatus !== settings.data.showOnlineStatus);

  const audienceSelect = (
    label: string,
    value: PrivacyAudience,
    onChange: (value: PrivacyAudience) => void,
    id: string,
    audiences: readonly PrivacyAudience[] = AUDIENCES,
  ) => (
    <div className={styles.privacyRow}>
      <label className={styles.privacyLabel} htmlFor={id}>
        {label}
      </label>
      <select
        id={id}
        className={styles.privacySelect}
        value={value}
        onChange={(e) => onChange(e.target.value as PrivacyAudience)}
      >
        {audiences.map((audience) => (
          <option key={audience} value={audience}>
            {AUDIENCE_LABEL[audience]}
          </option>
        ))}
      </select>
    </div>
  );

  return (
    <Panel label="PRIVACY">
      <div className={styles.panelBody}>
        {audienceSelect(
          "Who can send you friend requests",
          draft.friendRequests,
          (v) => setDraft({ ...draft, friendRequests: v }),
          "privacy-friend-requests",
          FRIEND_REQUEST_AUDIENCES,
        )}
        {audienceSelect(
          "Who can message you",
          draft.messages,
          (v) => setDraft({ ...draft, messages: v }),
          "privacy-messages",
        )}
        {audienceSelect(
          "Who can invite you to groups",
          draft.groupInvites,
          (v) => setDraft({ ...draft, groupInvites: v }),
          "privacy-group-invites",
        )}
        <label className={styles.privacyCheckbox}>
          <input
            type="checkbox"
            checked={draft.showOnlineStatus}
            onChange={(e) => setDraft({ ...draft, showOnlineStatus: e.target.checked })}
          />
          <span>Show my online status</span>
        </label>
        <div className={styles.privacyActions}>
          <Button
            size="sm"
            disabled={!dirty}
            loading={update.isPending}
            onClick={() =>
              update.mutate(draft, {
                onSuccess: () => show("success", "Privacy settings saved"),
                onError: (error) => showError(error, "Couldn't save privacy settings."),
              })
            }
          >
            SAVE
          </Button>
        </div>
      </div>
    </Panel>
  );
}

export default function SocialPage() {
  return (
    <Suspense>
      <SocialPageInner />
    </Suspense>
  );
}
