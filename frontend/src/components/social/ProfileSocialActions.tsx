"use client";

/**
 * The social actions block on someone else's profile — the natural entry point for
 * sending a friend request. Relationship state is derived from the loaded friends /
 * requests / blocks queries (a UX heuristic only — the server re-gates every action;
 * block and privacy denials come back as the same generic 403).
 */

import { Button } from "@/components/ui/Button";
import { useToast } from "@/components/ui/toast";
import { useChatDock } from "@/components/social/chat-dock-context";
import { useAuth } from "@/lib/auth/auth-context";
import {
  useAcceptFriendRequest,
  useBlockedUsers,
  useBlockUser,
  useDeleteFriendRequest,
  useFriendRequests,
  useFriends,
  useRemoveFriend,
  useSendFriendRequest,
  useUnblockUser,
} from "@/lib/hooks/use-social";

import styles from "./ProfileSocialActions.module.css";

export function ProfileSocialActions({
  userId,
  username,
}: {
  userId: string;
  username: string;
}) {
  const { isAuthenticated, currentUser } = useAuth();
  const { showError, show } = useToast();
  const { openDirectChat } = useChatDock();

  const friends = useFriends();
  const requests = useFriendRequests();
  const blocked = useBlockedUsers();

  const sendRequest = useSendFriendRequest();
  const acceptRequest = useAcceptFriendRequest();
  const deleteRequest = useDeleteFriendRequest();
  const removeFriend = useRemoveFriend();
  const blockUser = useBlockUser();
  const unblockUser = useUnblockUser();

  if (!isAuthenticated || currentUser?.id === userId) return null;

  const friendRows = friends.data?.pages.flatMap((p) => p.items) ?? [];
  const isFriend = friendRows.some((f) => f.userId === userId);
  const outgoing = requests.data?.outgoing.find((r) => r.addresseeId === userId);
  const incoming = requests.data?.incoming.find((r) => r.requesterId === userId);
  const isBlocked = (blocked.data ?? []).some((b) => b.userId === userId);

  if (isBlocked) {
    return (
      <div className={styles.actions}>
        <Button
          size="sm"
          variant="secondary"
          loading={unblockUser.isPending}
          onClick={() =>
            unblockUser.mutate(userId, {
              onError: (error) => showError(error, "Couldn't unblock the user."),
            })
          }
        >
          UNBLOCK
        </Button>
        <span className={styles.note}>You&apos;ve blocked @{username}.</span>
      </div>
    );
  }

  return (
    <div className={styles.actions}>
      {isFriend ? (
        <>
          <Button size="sm" onClick={() => void openDirectChat(userId, username)}>
            MESSAGE
          </Button>
          <Button
            size="sm"
            variant="ghost"
            loading={removeFriend.isPending}
            onClick={() => {
              if (!window.confirm(`Remove @${username} from your friends?`)) return;
              removeFriend.mutate(userId, {
                onError: (error) => showError(error, "Couldn't remove the friend."),
              });
            }}
          >
            REMOVE FRIEND
          </Button>
        </>
      ) : incoming ? (
        <>
          <Button
            size="sm"
            loading={acceptRequest.isPending}
            onClick={() =>
              acceptRequest.mutate(incoming.friendshipId, {
                onSuccess: () => show("success", `You're now friends with @${username}`),
                onError: (error) => showError(error, "Couldn't accept the request."),
              })
            }
          >
            ACCEPT REQUEST
          </Button>
          <Button
            size="sm"
            variant="ghost"
            loading={deleteRequest.isPending}
            onClick={() =>
              deleteRequest.mutate(incoming.friendshipId, {
                onError: (error) => showError(error, "Couldn't decline the request."),
              })
            }
          >
            DECLINE
          </Button>
        </>
      ) : outgoing ? (
        <Button
          size="sm"
          variant="ghost"
          loading={deleteRequest.isPending}
          onClick={() =>
            deleteRequest.mutate(outgoing.friendshipId, {
              onError: (error) => showError(error, "Couldn't cancel the request."),
            })
          }
        >
          CANCEL REQUEST
        </Button>
      ) : (
        <>
          <Button
            size="sm"
            loading={sendRequest.isPending}
            onClick={() =>
              sendRequest.mutate(userId, {
                onSuccess: () => show("success", "Friend request sent"),
                onError: (error) => showError(error, "Couldn't send the request."),
              })
            }
          >
            ADD FRIEND
          </Button>
          <Button size="sm" variant="secondary" onClick={() => void openDirectChat(userId, username)}>
            MESSAGE
          </Button>
        </>
      )}
      {!isBlocked ? (
        <Button
          size="sm"
          variant="ghost"
          loading={blockUser.isPending}
          onClick={() => {
            if (!window.confirm(`Block @${username}? They won't be able to contact you.`)) return;
            blockUser.mutate(userId, {
              onError: (error) => showError(error, "Couldn't block the user."),
            });
          }}
        >
          BLOCK
        </Button>
      ) : null}
    </div>
  );
}
