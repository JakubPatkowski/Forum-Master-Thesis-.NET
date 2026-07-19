"use client";

/**
 * The floating dock itself: minimized pills first, then the open windows (design:
 * Social.dc.html). ONE presence poll covers every open DM's other participant, and
 * unread badges come from the (push-invalidated) conversations query — the dock never
 * fires per-chat requests for either.
 */

import { ChatPill } from "@/components/social/ChatPill";
import { ChatWindow } from "@/components/social/ChatWindow";
import { useChatDock } from "@/components/social/chat-dock-context";
import { usePresence } from "@/lib/hooks/use-presence";
import { useConversations } from "@/lib/hooks/use-social";
import { statusOf } from "@/lib/social/presence";

import styles from "./chat.module.css";

export function ChatDock() {
  const { chats, minimize, restore, close } = useChatDock();
  const conversations = useConversations();

  const otherUserIds = chats
    .filter((c) => c.kind === "direct" && c.otherUserId)
    .map((c) => c.otherUserId!);
  const presence = usePresence(otherUserIds);

  if (chats.length === 0) return null;

  const unreadOf = (conversationId: string) =>
    conversations.data?.find((c) => c.conversationId === conversationId)?.unreadCount ?? 0;

  return (
    <div className={styles.dock}>
      {chats
        .filter((c) => c.minimized)
        .map((chat) => (
          <ChatPill
            key={chat.conversationId}
            chat={chat}
            unreadCount={unreadOf(chat.conversationId)}
            status={
              chat.otherUserId ? statusOf(presence.data ?? new Map(), chat.otherUserId) : undefined
            }
            onRestore={() => restore(chat.conversationId)}
          />
        ))}
      {chats
        .filter((c) => !c.minimized)
        .map((chat) => (
          <ChatWindow
            key={chat.conversationId}
            chat={chat}
            status={
              chat.otherUserId ? statusOf(presence.data ?? new Map(), chat.otherUserId) : undefined
            }
            onMinimize={() => minimize(chat.conversationId)}
            onClose={() => close(chat.conversationId)}
          />
        ))}
    </div>
  );
}
