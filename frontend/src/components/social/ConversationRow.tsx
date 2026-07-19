"use client";

/**
 * One conversation-list entry: avatar/group icon, display name, last-message preview
 * and MY unread badge (never a sender-visible receipt). Clicking opens the chat in
 * the floating dock.
 */

import { PresenceAvatar } from "@/components/social/PresenceAvatar";
import { GroupIcon } from "@/components/ui/GroupIcon";
import type { ConversationResponse, PresenceStatus } from "@/lib/api/types";
import { timeAgoLabel } from "@/lib/utils/time";

import styles from "./rows.module.css";

export function ConversationRow({
  conversation,
  status,
  onOpen,
}: {
  conversation: ConversationResponse;
  /** Presence of the other participant (direct conversations only). */
  status?: PresenceStatus;
  onOpen: () => void;
}) {
  return (
    <button className={styles.row} onClick={onOpen}>
      {conversation.type === "direct" && conversation.otherUserId ? (
        <PresenceAvatar
          userId={conversation.otherUserId}
          username={conversation.displayName}
          status={status}
        />
      ) : (
        <GroupIcon
          groupId={conversation.groupId ?? conversation.conversationId}
          name={conversation.displayName}
          size={34}
        />
      )}
      <span className={styles.text}>
        <span className={styles.name}>{conversation.displayName}</span>
        <span className={styles.sub}>
          {conversation.lastMessagePreview ?? "No messages yet"}
          {conversation.lastMessageOnUtc ? ` · ${timeAgoLabel(conversation.lastMessageOnUtc)}` : ""}
        </span>
      </span>
      {conversation.unreadCount > 0 ? (
        <span className={styles.unreadBadge}>
          {conversation.unreadCount > 99 ? "99+" : conversation.unreadCount}
        </span>
      ) : null}
    </button>
  );
}
