"use client";

/**
 * App-level owner of the floating multi-chat dock (design: Social.dc.html): a row of
 * minimized pills plus up to MAX_OPEN_WINDOWS simultaneously open windows, alive
 * across route changes. Opening a DM goes through POST /conversations/direct
 * (get-or-create, privacy/block gated — a 403 here is the deliberately generic
 * denial); a group chat's conversation id IS the group id, so it opens directly.
 */

import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { useQueryClient } from "@tanstack/react-query";

import { ChatDock } from "@/components/social/ChatDock";
import { useToast } from "@/components/ui/toast";
import { queryKeys } from "@/lib/api/keys";
import { socialApi } from "@/lib/api/social";
import type { ConversationResponse, ConversationType } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/auth-context";

export interface OpenChat {
  conversationId: string;
  kind: ConversationType;
  title: string;
  /** Direct chats only — drives the header presence dot. */
  otherUserId?: string;
  minimized: boolean;
}

interface ChatDockContextValue {
  chats: OpenChat[];
  /** Get-or-create the DM with a user, then open its window. */
  openDirectChat: (userId: string, username: string) => Promise<void>;
  /** Open a chat for an already-known conversation row. */
  openConversation: (conversation: ConversationResponse) => void;
  /** Open a group's chat (conversation id == group id). */
  openGroupChat: (groupId: string, name: string) => void;
  minimize: (conversationId: string) => void;
  restore: (conversationId: string) => void;
  close: (conversationId: string) => void;
}

const ChatDockContext = createContext<ChatDockContextValue | null>(null);

/** More windows than this and the oldest open one collapses into a pill. */
const MAX_OPEN_WINDOWS = 3;

function withOpened(chats: OpenChat[], next: OpenChat): OpenChat[] {
  const existing = chats.find((c) => c.conversationId === next.conversationId);
  let list = existing
    ? chats.map((c) =>
        c.conversationId === next.conversationId ? { ...c, minimized: false } : c,
      )
    : [...chats, next];
  const open = list.filter((c) => !c.minimized);
  if (open.length > MAX_OPEN_WINDOWS) {
    const oldest = open[0]!;
    list = list.map((c) =>
      c.conversationId === oldest.conversationId ? { ...c, minimized: true } : c,
    );
  }
  return list;
}

export function ChatDockProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth();
  const { showError } = useToast();
  const queryClient = useQueryClient();
  const [chats, setChats] = useState<OpenChat[]>([]);

  const openConversation = useCallback((conversation: ConversationResponse) => {
    setChats((list) =>
      withOpened(list, {
        conversationId: conversation.conversationId,
        kind: conversation.type,
        title: conversation.displayName,
        otherUserId: conversation.otherUserId ?? undefined,
        minimized: false,
      }),
    );
  }, []);

  const openDirectChat = useCallback(
    async (userId: string, username: string) => {
      try {
        const { conversationId } = await socialApi.openDirectConversation(userId);
        void queryClient.invalidateQueries({ queryKey: queryKeys.conversations });
        setChats((list) =>
          withOpened(list, {
            conversationId,
            kind: "direct",
            title: username,
            otherUserId: userId,
            minimized: false,
          }),
        );
      } catch (error) {
        showError(error, "Couldn't open the conversation.");
      }
    },
    [queryClient, showError],
  );

  const openGroupChat = useCallback((groupId: string, name: string) => {
    setChats((list) =>
      withOpened(list, {
        conversationId: groupId,
        kind: "group",
        title: name,
        minimized: false,
      }),
    );
  }, []);

  const minimize = useCallback((conversationId: string) => {
    setChats((list) =>
      list.map((c) => (c.conversationId === conversationId ? { ...c, minimized: true } : c)),
    );
  }, []);

  const restore = useCallback((conversationId: string) => {
    setChats((list) => {
      const chat = list.find((c) => c.conversationId === conversationId);
      return chat ? withOpened(list, chat) : list;
    });
  }, []);

  const close = useCallback((conversationId: string) => {
    setChats((list) => list.filter((c) => c.conversationId !== conversationId));
  }, []);

  const value = useMemo<ChatDockContextValue>(
    () => ({ chats, openDirectChat, openConversation, openGroupChat, minimize, restore, close }),
    [chats, openDirectChat, openConversation, openGroupChat, minimize, restore, close],
  );

  return (
    <ChatDockContext.Provider value={value}>
      {children}
      {isAuthenticated ? <ChatDock /> : null}
    </ChatDockContext.Provider>
  );
}

export function useChatDock(): ChatDockContextValue {
  const context = useContext(ChatDockContext);
  if (!context) throw new Error("useChatDock must be used within ChatDockProvider");
  return context;
}
