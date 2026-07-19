"use client";

/**
 * One floating chat window (DM or group chat). Realtime: subscribes the conversation
 * view while mounted — pushes invalidate the messages/conversations queries, this
 * window only re-fetches (fetch-then-patch). History is the newest-first keyset feed
 * rendered oldest→newest with LOAD OLDER ↑ on top. Opening/receiving while open
 * stamps MY read position (never a sender-visible receipt).
 *
 * The composer's IMAGE button runs the real Files flow (initiate → presigned PUT →
 * commit), inserts the `![name](image:<fileId>)` token at the draft's end, and the
 * committed files are attached to the message (targetType "message") right after send
 * — the same composer-attaches-READY-files pattern the thread composer uses.
 */

import Link from "next/link";
import { useEffect, useMemo, useRef, useState, type ChangeEvent } from "react";

import { MessageBubble } from "@/components/social/MessageBubble";
import { PresenceAvatar } from "@/components/social/PresenceAvatar";
import type { OpenChat } from "@/components/social/chat-dock-context";
import { GroupIcon } from "@/components/ui/GroupIcon";
import { useToast } from "@/components/ui/toast";
import { filesApi } from "@/lib/api/files";
import { ALLOWED_UPLOAD_TYPES, MAX_MESSAGE_LENGTH, type PresenceStatus } from "@/lib/api/types";
import { useAuth } from "@/lib/auth/auth-context";
import {
  useDeleteMessage,
  useEditMessage,
  useGroup,
  useMarkConversationRead,
  useMessages,
  useSendMessage,
} from "@/lib/hooks/use-social";
import { imageToken } from "@/lib/markdown/media-convention";
import { presenceLabel } from "@/lib/social/presence";
import { useRealtimeSubscription } from "@/lib/realtime/realtime-context";
import { uploadFile } from "@/lib/upload/upload";

import styles from "./chat.module.css";

/** Scroll glue: stick to the bottom unless the reader scrolled up further than this. */
const NEAR_BOTTOM_PX = 120;

export function ChatWindow({
  chat,
  status,
  onMinimize,
  onClose,
}: {
  chat: OpenChat;
  /** Presence of the other participant (direct chats only). */
  status?: PresenceStatus;
  onMinimize: () => void;
  onClose: () => void;
}) {
  const { currentUser } = useAuth();
  const { showError, show } = useToast();
  const bodyRef = useRef<HTMLDivElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [draft, setDraft] = useState("");
  const [sending, setSending] = useState(false);
  const [uploading, setUploading] = useState(false);
  // Files already committed for this draft — attached to the message after send.
  const pendingFileIds = useRef<string[]>([]);

  useRealtimeSubscription("conversation", chat.conversationId);

  const messagesQuery = useMessages(chat.conversationId);
  const sendMessage = useSendMessage(chat.conversationId);
  const editMessage = useEditMessage(chat.conversationId);
  const deleteMessage = useDeleteMessage(chat.conversationId);
  const markRead = useMarkConversationRead();

  // Group chats: the viewer's admin bit gates delete-any (conversation id == group id).
  const group = useGroup(chat.kind === "group" ? chat.conversationId : null);
  const canModerate = chat.kind === "group" && (group.data?.isAdmin ?? false);

  // Keyset pages are newest-first; the window renders oldest→newest.
  const messages = useMemo(
    () => [...(messagesQuery.data?.pages.flatMap((p) => p.items) ?? [])].reverse(),
    [messagesQuery.data],
  );
  const newestId = messages.length > 0 ? messages[messages.length - 1]!.messageId : null;

  // Stamp my read position when the window opens and whenever a new message lands
  // while it is open. Idempotent, cheap, and never visible to the sender.
  const conversationId = chat.conversationId;
  useEffect(() => {
    if (newestId === null) return;
    markRead.mutate(conversationId);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- markRead is a stable mutation handle
  }, [conversationId, newestId]);

  // Stick to the bottom on new messages unless the reader scrolled up into history.
  const prevNewestRef = useRef<string | null>(null);
  useEffect(() => {
    const el = bodyRef.current;
    if (!el || newestId === null) return;
    const isFirstFill = prevNewestRef.current === null;
    const nearBottom = el.scrollHeight - (el.scrollTop + el.clientHeight) < NEAR_BOTTOM_PX;
    if (isFirstFill || nearBottom) el.scrollTop = el.scrollHeight;
    prevNewestRef.current = newestId;
  }, [newestId]);

  const send = async () => {
    const body = draft.trim();
    if (!body || sending) return;
    setSending(true);
    try {
      const sent = await sendMessage.mutateAsync(body);
      const fileIds = pendingFileIds.current;
      pendingFileIds.current = [];
      setDraft("");
      for (const fileId of fileIds) {
        try {
          await filesApi.attach(fileId, { targetType: "message", targetId: sent.messageId });
        } catch {
          show("warning", "An image didn't attach", "The message was sent without it.");
        }
      }
    } catch (error) {
      showError(error, "Couldn't send the message.");
    } finally {
      setSending(false);
    }
  };

  const onPickImage = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    setUploading(true);
    try {
      const committed = await uploadFile(file, () => {});
      pendingFileIds.current.push(committed.fileId);
      setDraft((d) => `${d}${d && !d.endsWith("\n") ? "\n" : ""}${imageToken(committed.fileId, file.name)}\n`);
    } catch (error) {
      showError(error);
    } finally {
      setUploading(false);
    }
  };

  const headerSubClass =
    chat.kind === "direct"
      ? status === "online"
        ? `${styles.headerSub} ${styles.headerSubOnline}`
        : status === "away"
          ? `${styles.headerSub} ${styles.headerSubAway}`
          : styles.headerSub
      : styles.headerSub;

  return (
    <div className={styles.window}>
      <div className={styles.header}>
        {chat.kind === "direct" && chat.otherUserId ? (
          <PresenceAvatar
            userId={chat.otherUserId}
            username={chat.title}
            status={status}
            size={32}
          />
        ) : (
          <GroupIcon groupId={chat.conversationId} name={chat.title} size={32} />
        )}
        <span className={styles.headerText}>
          {chat.kind === "direct" && chat.otherUserId ? (
            <Link href={`/u/${chat.otherUserId}`} className={styles.headerName}>
              {chat.title}
            </Link>
          ) : (
            <Link href={`/social?group=${chat.conversationId}`} className={styles.headerName}>
              {chat.title}
            </Link>
          )}
          <span className={headerSubClass}>
            {chat.kind === "direct" ? presenceLabel(status ?? "offline") : "group chat"}
          </span>
        </span>
        <button className={styles.headerButton} title="Minimize" onClick={onMinimize}>
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
            <path d="M5 12h14" />
          </svg>
        </button>
        <button
          className={`${styles.headerButton} ${styles.headerButtonDanger}`}
          title="Close"
          onClick={onClose}
        >
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
            <path d="M18 6 6 18M6 6l12 12" />
          </svg>
        </button>
      </div>

      <div className={`${styles.body} panel-scroll`} ref={bodyRef}>
        <span className={styles.bodyNote}>WS DELIVERY · FETCH-THEN-PATCH</span>
        {messagesQuery.hasNextPage ? (
          <button
            className={styles.loadOlder}
            onClick={() => void messagesQuery.fetchNextPage()}
            disabled={messagesQuery.isFetchingNextPage}
          >
            {messagesQuery.isFetchingNextPage ? "LOADING…" : "LOAD OLDER ↑"}
          </button>
        ) : null}
        {messagesQuery.isLoading ? (
          <span className={styles.emptyNote}>Loading…</span>
        ) : messages.length === 0 ? (
          <span className={styles.emptyNote}>No messages yet — say hi.</span>
        ) : (
          messages.map((message, index) => {
            const isOwn = message.senderId === currentUser?.id;
            const previous = index > 0 ? messages[index - 1] : undefined;
            return (
              <MessageBubble
                key={message.messageId}
                message={message}
                isOwn={isOwn}
                showAuthor={chat.kind === "group" && previous?.senderId !== message.senderId}
                canEdit={isOwn}
                canDelete={isOwn || canModerate}
                onEdit={(body) =>
                  editMessage.mutate(
                    { messageId: message.messageId, body },
                    { onError: (error) => showError(error, "Couldn't edit the message.") },
                  )
                }
                onDelete={() =>
                  deleteMessage.mutate(message.messageId, {
                    onError: (error) => showError(error, "Couldn't delete the message."),
                  })
                }
              />
            );
          })
        )}
      </div>

      <div className={styles.composer}>
        <button
          className={styles.composerButton}
          title="Attach an image"
          onClick={() => fileInputRef.current?.click()}
          disabled={uploading}
        >
          <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor">
            <path d="M21 19V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2zM8.5 11 11 14l3.5-4.5L19 16H5l3.5-5z" />
          </svg>
        </button>
        <textarea
          className={styles.composerInput}
          value={draft}
          maxLength={MAX_MESSAGE_LENGTH}
          rows={1}
          placeholder="Message… (markdown)"
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" && !e.shiftKey) {
              e.preventDefault();
              void send();
            }
          }}
        />
        <button
          className={styles.sendButton}
          title="Send"
          onClick={() => void send()}
          disabled={sending || draft.trim().length === 0}
        >
          <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
            <path d="M3 20.5 21 12 3 3.5 3 10l12 2-12 2z" />
          </svg>
        </button>
        <input
          ref={fileInputRef}
          type="file"
          accept={ALLOWED_UPLOAD_TYPES.join(",")}
          className={styles.hiddenInput}
          onChange={(e) => void onPickImage(e)}
          aria-hidden
          tabIndex={-1}
        />
      </div>
    </div>
  );
}
