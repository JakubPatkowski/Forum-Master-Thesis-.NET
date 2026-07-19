"use client";

/**
 * One chat message. Body is markdown rendered through the app's sanitizing renderer
 * (inline `image:<fileId>` tokens resolve like everywhere else — message-attached
 * files are participant-gated server-side). A deleted message arrives tombstoned as
 * the literal "[deleted]" and keeps its place, styled muted like a removed comment.
 *
 * Controls (backend invariants, not styling choices): edit+delete on your OWN
 * messages; delete-only on others' messages when you administer a GROUP conversation;
 * never anything on someone else's DM message.
 */

import { useState } from "react";

import { MarkdownView } from "@/components/markdown/MarkdownView";
import { Avatar } from "@/components/ui/Avatar";
import type { MessageResponse } from "@/lib/api/types";
import { MAX_MESSAGE_LENGTH } from "@/lib/api/types";
import { timeAgoLabel } from "@/lib/utils/time";

import styles from "./chat.module.css";

export function MessageBubble({
  message,
  isOwn,
  showAuthor,
  canEdit,
  canDelete,
  onEdit,
  onDelete,
}: {
  message: MessageResponse;
  isOwn: boolean;
  /** Author line (small avatar + name) — group chats, on author change only. */
  showAuthor: boolean;
  canEdit: boolean;
  canDelete: boolean;
  onEdit: (body: string) => void;
  onDelete: () => void;
}) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(message.body);

  const submitEdit = () => {
    const body = draft.trim();
    setEditing(false);
    if (body && body !== message.body) onEdit(body);
  };

  return (
    <div className={isOwn ? styles.bubbleRowMe : styles.bubbleRow}>
      <div className={styles.bubbleCol}>
        {showAuthor && !isOwn ? (
          <span className={styles.author}>
            <Avatar userId={message.senderId} displayName={message.senderUsername} size={16} />
            <span className={styles.authorName}>{message.senderUsername}</span>
          </span>
        ) : null}

        {editing ? (
          <textarea
            className={styles.editArea}
            value={draft}
            maxLength={MAX_MESSAGE_LENGTH}
            rows={3}
            autoFocus
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                submitEdit();
              }
              if (e.key === "Escape") {
                setEditing(false);
                setDraft(message.body);
              }
            }}
          />
        ) : (
          <div
            className={[
              isOwn ? styles.bubbleMe : styles.bubble,
              message.isDeleted ? styles.bubbleDeleted : undefined,
            ]
              .filter(Boolean)
              .join(" ")}
          >
            {message.isDeleted ? message.body : <MarkdownView markdown={message.body} />}
          </div>
        )}

        <span className={styles.bubbleFooter}>
          <span className={styles.bubbleTime}>
            {timeAgoLabel(message.sentOnUtc)}
            {message.editedOnUtc && !message.isDeleted ? " · EDITED" : ""}
          </span>
          {!message.isDeleted && !editing && canEdit ? (
            <button
              className={styles.bubbleAction}
              onClick={() => {
                setDraft(message.body);
                setEditing(true);
              }}
            >
              EDIT
            </button>
          ) : null}
          {!message.isDeleted && !editing && canDelete ? (
            <button
              className={`${styles.bubbleAction} ${styles.bubbleActionDanger}`}
              onClick={onDelete}
            >
              DELETE
            </button>
          ) : null}
        </span>
      </div>
    </div>
  );
}
