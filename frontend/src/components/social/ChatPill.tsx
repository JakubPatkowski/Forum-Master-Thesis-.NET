"use client";

/**
 * A minimized chat as a dock pill: avatar/icon, name, unread badge. While minimized
 * the conversation stays subscribed (the pill mounts the subscription) so the badge
 * keeps counting live for group chats too — DMs would reach us via the user view
 * anyway, group messages only via the conversation/group views.
 */

import { PresenceAvatar } from "@/components/social/PresenceAvatar";
import type { OpenChat } from "@/components/social/chat-dock-context";
import { GroupIcon } from "@/components/ui/GroupIcon";
import type { PresenceStatus } from "@/lib/api/types";
import { useRealtimeSubscription } from "@/lib/realtime/realtime-context";

import styles from "./chat.module.css";

export function ChatPill({
  chat,
  unreadCount,
  status,
  onRestore,
}: {
  chat: OpenChat;
  unreadCount: number;
  status?: PresenceStatus;
  onRestore: () => void;
}) {
  useRealtimeSubscription("conversation", chat.conversationId);

  return (
    <button className={styles.pill} onClick={onRestore} title={`Open chat with ${chat.title}`}>
      {chat.kind === "direct" && chat.otherUserId ? (
        <PresenceAvatar userId={chat.otherUserId} username={chat.title} status={status} size={28} />
      ) : (
        <GroupIcon groupId={chat.conversationId} name={chat.title} size={28} />
      )}
      <span className={styles.pillName}>{chat.title}</span>
      {unreadCount > 0 ? (
        <span className={styles.pillBadge}>{unreadCount > 99 ? "99+" : unreadCount}</span>
      ) : null}
    </button>
  );
}
