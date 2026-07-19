"use client";

/**
 * A group's detail view: header (icon, name, badges, description), role-aware actions
 * (join / leave / open chat / invite / edit / delete), and the keyset member list with
 * ONE presence poll across the loaded page. Realtime: subscribes the group view while
 * mounted — group renames, member joins/leaves and group-chat messages all route
 * there.
 *
 * Backend invariants surfaced here, not invented: the owner can never leave or be
 * kicked (transfer or delete instead); `isAdmin` on the detail/member DTOs already
 * resolves owner-OR-admin; private only gates discovery/join.
 */

import { useMemo, useState } from "react";

import { FriendRow } from "@/components/social/FriendRow";
import { GroupMemberRow } from "@/components/social/GroupMemberRow";
import { GroupModal } from "@/components/social/GroupModal";
import { SocialRowSkeleton } from "@/components/social/SocialRowSkeleton";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { ApiErrorState } from "@/components/ui/ErrorState";
import { GroupIcon } from "@/components/ui/GroupIcon";
import { LoadMoreButton } from "@/components/ui/LoadMoreButton";
import { Modal } from "@/components/ui/Modal";
import { Panel } from "@/components/ui/Panel";
import { useToast } from "@/components/ui/toast";
import { ApiError } from "@/lib/api/problem";
import { useAuth } from "@/lib/auth/auth-context";
import { usePresence } from "@/lib/hooks/use-presence";
import {
  useDeleteGroup,
  useFriends,
  useGroup,
  useGroupMembers,
  useInviteToGroup,
  useJoinGroup,
  useKickGroupMember,
  useLeaveGroup,
  useSetGroupMemberRole,
  useTransferGroupOwnership,
} from "@/lib/hooks/use-social";
import { statusOf } from "@/lib/social/presence";
import { useRealtimeSubscription } from "@/lib/realtime/realtime-context";
import { timeAgoLabel } from "@/lib/utils/time";

import styles from "./GroupDetailPanel.module.css";

export function GroupDetailPanel({
  groupId,
  onOpenChat,
  onMessageUser,
  onClose,
}: {
  groupId: string;
  onOpenChat: (groupId: string, name: string) => void;
  onMessageUser: (userId: string, username: string) => void;
  /** Back to the overview (deselect the group). */
  onClose: () => void;
}) {
  const { currentUser } = useAuth();
  const { showError, show } = useToast();

  useRealtimeSubscription("group", groupId);

  const group = useGroup(groupId);
  const members = useGroupMembers(groupId);
  const memberRows = useMemo(
    () => members.data?.pages.flatMap((p) => p.items) ?? [],
    [members.data],
  );
  const presence = usePresence(memberRows.map((m) => m.userId));

  const joinGroup = useJoinGroup();
  const leaveGroup = useLeaveGroup();
  const deleteGroup = useDeleteGroup();
  const kickMember = useKickGroupMember(groupId);
  const setRole = useSetGroupMemberRole(groupId);
  const transferOwnership = useTransferGroupOwnership(groupId);

  const [editOpen, setEditOpen] = useState(false);
  const [inviteOpen, setInviteOpen] = useState(false);

  if (group.isLoading) {
    return (
      <Panel label="GROUP" accent="cyan">
        <div className={styles.body}>
          <SocialRowSkeleton />
          <SocialRowSkeleton />
        </div>
      </Panel>
    );
  }
  if (group.isError || !group.data) {
    return (
      <ApiErrorState
        error={
          group.error instanceof ApiError
            ? group.error
            : new ApiError(0, "Couldn't load the group.", null, "Unknown")
        }
        onRetry={() => void group.refetch()}
      />
    );
  }

  const detail = group.data;
  const isOwner = currentUser?.id === detail.ownerId;

  const onLeave = () => {
    if (!window.confirm(`Leave ${detail.name}? You lose access to its chat.`)) return;
    leaveGroup.mutate(groupId, {
      onError: (error) => showError(error, "Couldn't leave the group."),
    });
  };

  const onDelete = () => {
    if (!window.confirm(`Delete ${detail.name}? The group and its chat disappear for everyone.`))
      return;
    deleteGroup.mutate(groupId, {
      onSuccess: () => {
        show("success", "Group deleted");
        onClose();
      },
      onError: (error) => showError(error, "Couldn't delete the group."),
    });
  };

  return (
    <Panel
      label="GROUP"
      accent="cyan"
      headerExtra={
        <button className={styles.backButton} onClick={onClose}>
          ← BACK
        </button>
      }
    >
      <div className={styles.body}>
        <div className={styles.head}>
          <GroupIcon groupId={detail.groupId} name={detail.name} size={56} />
          <div className={styles.headText}>
            <div className={styles.titleRow}>
              <span className={styles.title}>{detail.name}</span>
              {detail.visibility === "private" ? <Badge>PRIVATE</Badge> : null}
              {isOwner ? (
                <Badge tone="accent">OWNER</Badge>
              ) : detail.isAdmin ? (
                <Badge tone="cyan">ADMIN</Badge>
              ) : detail.isMember ? (
                <Badge tone="cyan">MEMBER</Badge>
              ) : null}
            </div>
            <div className={styles.meta}>
              {detail.memberCount} {detail.memberCount === 1 ? "member" : "members"} · owned by @
              {detail.ownerUsername} · created {timeAgoLabel(detail.createdOnUtc)} ago
            </div>
            {detail.description ? (
              <p className={styles.description}>{detail.description}</p>
            ) : null}
          </div>
        </div>

        <div className={styles.actions}>
          {detail.isMember ? (
            <Button size="sm" onClick={() => onOpenChat(detail.groupId, detail.name)}>
              OPEN CHAT
            </Button>
          ) : detail.visibility === "public" ? (
            <Button
              size="sm"
              loading={joinGroup.isPending}
              onClick={() =>
                joinGroup.mutate(groupId, {
                  onError: (error) => showError(error, "Couldn't join the group."),
                })
              }
            >
              JOIN GROUP
            </Button>
          ) : (
            <span className={styles.inviteOnlyNote}>INVITE-ONLY</span>
          )}
          {detail.isAdmin ? (
            <>
              <Button size="sm" variant="secondary" onClick={() => setInviteOpen(true)}>
                INVITE
              </Button>
              <Button size="sm" variant="ghost" onClick={() => setEditOpen(true)}>
                EDIT
              </Button>
            </>
          ) : null}
          {detail.isMember && !isOwner ? (
            <Button size="sm" variant="ghost" loading={leaveGroup.isPending} onClick={onLeave}>
              LEAVE
            </Button>
          ) : null}
          {isOwner ? (
            <Button size="sm" variant="danger" loading={deleteGroup.isPending} onClick={onDelete}>
              DELETE
            </Button>
          ) : null}
        </div>
        {isOwner ? (
          <div className={styles.ownerNote}>
            The owner can&apos;t leave — transfer ownership from a member&apos;s menu, or delete
            the group.
          </div>
        ) : null}

        <div className={styles.membersLabel}>MEMBERS</div>
        {detail.isMember || memberRows.length > 0 ? (
          <>
            {members.isLoading ? (
              <>
                <SocialRowSkeleton />
                <SocialRowSkeleton />
              </>
            ) : (
              memberRows.map((member) => (
                <GroupMemberRow
                  key={member.userId}
                  member={member}
                  status={statusOf(presence.data ?? new Map(), member.userId)}
                  isSelf={member.userId === currentUser?.id}
                  viewerIsAdmin={detail.isAdmin}
                  viewerIsOwner={isOwner}
                  onMessage={() => onMessageUser(member.userId, member.username)}
                  onSetRole={(role) =>
                    setRole.mutate(
                      { userId: member.userId, role },
                      { onError: (error) => showError(error, "Couldn't change the role.") },
                    )
                  }
                  onKick={() => {
                    if (!window.confirm(`Kick @${member.username} from ${detail.name}?`)) return;
                    kickMember.mutate(member.userId, {
                      onError: (error) => showError(error, "Couldn't kick the member."),
                    });
                  }}
                  onTransferOwnership={() => {
                    if (
                      !window.confirm(
                        `Make @${member.username} the owner of ${detail.name}? You become a regular admin.`,
                      )
                    )
                      return;
                    transferOwnership.mutate(member.userId, {
                      onError: (error) => showError(error, "Couldn't transfer ownership."),
                    });
                  }}
                />
              ))
            )}
            <LoadMoreButton
              onClick={() => void members.fetchNextPage()}
              loading={members.isFetchingNextPage}
              hasMore={members.hasNextPage ?? false}
            />
          </>
        ) : (
          <div className={styles.membersHidden}>The member list is visible to members only.</div>
        )}
      </div>

      {editOpen ? (
        <GroupModal mode="edit" group={detail} onClose={() => setEditOpen(false)} />
      ) : null}
      {inviteOpen ? (
        <InviteFriendsModal
          groupId={groupId}
          groupName={detail.name}
          memberIds={memberRows.map((m) => m.userId)}
          onClose={() => setInviteOpen(false)}
        />
      ) : null}
    </Panel>
  );
}

/** Pick friends to invite — the natural source of target user ids. */
function InviteFriendsModal({
  groupId,
  groupName,
  memberIds,
  onClose,
}: {
  groupId: string;
  groupName: string;
  memberIds: string[];
  onClose: () => void;
}) {
  const { showError } = useToast();
  const friends = useFriends();
  const invite = useInviteToGroup(groupId);
  const [invitedIds, setInvitedIds] = useState<Set<string>>(new Set());

  const rows = (friends.data?.pages.flatMap((p) => p.items) ?? []).filter(
    (f) => !memberIds.includes(f.userId),
  );

  return (
    <Modal
      open
      onClose={onClose}
      title="Invite friends"
      subtitle={`POST /api/social/groups/{id}/invites · ${groupName}`}
      width={520}
    >
      {friends.isLoading ? (
        <>
          <SocialRowSkeleton />
          <SocialRowSkeleton />
        </>
      ) : rows.length === 0 ? (
        <div className={styles.membersHidden}>
          No friends left to invite — everyone you know is already in.
        </div>
      ) : (
        rows.map((friend) => (
          <div key={friend.userId} className={styles.inviteRow}>
            <FriendRow userId={friend.userId} username={friend.username} />
            <Button
              size="sm"
              variant="secondary"
              disabled={invitedIds.has(friend.userId)}
              onClick={() =>
                invite.mutate(friend.userId, {
                  onSuccess: () => setInvitedIds((s) => new Set(s).add(friend.userId)),
                  onError: (error) => showError(error, "Couldn't send the invite."),
                })
              }
            >
              {invitedIds.has(friend.userId) ? "INVITED" : "INVITE"}
            </Button>
          </div>
        ))
      )}
      <LoadMoreButton
        onClick={() => void friends.fetchNextPage()}
        loading={friends.isFetchingNextPage}
        hasMore={friends.hasNextPage ?? false}
      />
    </Modal>
  );
}
