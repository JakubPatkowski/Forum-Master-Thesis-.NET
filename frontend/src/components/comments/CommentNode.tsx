"use client";

/**
 * One node of the comment tree (design: Thread.dc.html). The API's flat, path-ordered
 * list renders directly — indentation is depth-based with a cap (responsive
 * degradation). Soft-deleted comments stay in place as a greyed "[deleted]" tombstone
 * with children nested under them and author fields untouched. Reply is disabled at
 * depth 5 up front (the 422 remains the real gate).
 */

import Link from "next/link";
import { useState } from "react";

import { MarkdownEditor } from "@/components/compose/MarkdownEditor";
import { ReactionButton } from "@/components/engagement/ReactionButton";
import { MarkdownView } from "@/components/markdown/MarkdownView";
import { Avatar } from "@/components/ui/Avatar";
import { Badge } from "@/components/ui/Badge";
import { Button } from "@/components/ui/Button";
import { LiveDot } from "@/components/ui/LiveDot";
import type { CommentResponse, ReactionSummaryResponse } from "@/lib/api/types";
import { canReply, indentPx } from "@/lib/comments/tree";
import { timeAgoLabel } from "@/lib/utils/time";

import styles from "./CommentNode.module.css";

export interface CommentNodeProps {
  comment: CommentResponse;
  threadOwnerId: string;
  currentUserId: string | null;
  isModerator: boolean;
  reaction?: ReactionSummaryResponse;
  isNew?: boolean;
  onReply: (parentId: string, body: string) => Promise<void>;
  onEdit: (commentId: string, body: string) => Promise<void>;
  onDelete: (commentId: string) => Promise<void>;
  /** Uploads a picked image via the Files flow and resolves its fileId (for inline media). */
  onUploadImage?: (file: File) => Promise<string | null>;
}

export function CommentNode({
  comment,
  threadOwnerId,
  currentUserId,
  isModerator,
  reaction,
  isNew = false,
  onReply,
  onEdit,
  onDelete,
  onUploadImage,
}: CommentNodeProps) {
  const [replying, setReplying] = useState(false);
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState("");
  const [editDraft, setEditDraft] = useState(comment.body);
  const [busy, setBusy] = useState(false);

  const isOwn = currentUserId !== null && currentUserId === comment.ownerId;
  const isOp = comment.ownerId === threadOwnerId;
  const mayModify = !comment.isDeleted && (isOwn || isModerator);

  const submitReply = async () => {
    if (!draft.trim()) return;
    setBusy(true);
    try {
      await onReply(comment.id, draft.trim());
      setDraft("");
      setReplying(false);
    } finally {
      setBusy(false);
    }
  };

  const submitEdit = async () => {
    if (!editDraft.trim()) return;
    setBusy(true);
    try {
      await onEdit(comment.id, editDraft.trim());
      setEditing(false);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div
      id={`comment-${comment.id}`}
      style={{ marginLeft: indentPx(comment.depth) }}
      className={styles.slot}
    >
      <div className={styles.row}>
        {comment.depth > 0 ? <span className={styles.elbow} aria-hidden /> : null}
        <div className={isNew ? `${styles.card} ${styles.cardNew}` : styles.card}>
          <div className={styles.header}>
            <Avatar userId={comment.ownerId} displayName={comment.displayName} size={26} />
            <Link href={`/u/${comment.ownerId}`} className={styles.author}>
              {comment.displayName}
            </Link>
            <span className={styles.meta}>
              @{comment.username} · {timeAgoLabel(comment.createdOnUtc)}
            </span>
            {isOp ? <Badge tone="accent">OP</Badge> : null}
            {isOwn ? <Badge>YOU</Badge> : null}
            {isNew ? (
              <Badge tone="cyan">
                <LiveDot color="cyan" size={5} />
                NEW
              </Badge>
            ) : null}
          </div>

          {comment.isDeleted ? (
            <p className={styles.deleted}>[deleted]</p>
          ) : editing ? (
            <div className={styles.editBox}>
              <MarkdownEditor
                value={editDraft}
                onChange={setEditDraft}
                rows={3}
                compact
                placeholder="Edit your comment… Markdown & inline media supported."
                onUploadImage={onUploadImage}
              />
              <div className={styles.editActions}>
                <Button size="sm" variant="ghost" onClick={() => setEditing(false)}>
                  Cancel
                </Button>
                <Button
                  size="sm"
                  onClick={() => void submitEdit()}
                  disabled={!editDraft.trim()}
                  loading={busy}
                >
                  Save
                </Button>
              </div>
            </div>
          ) : (
            <MarkdownView markdown={comment.body} className={styles.body} />
          )}

          <div className={styles.actions}>
            {!comment.isDeleted ? (
              <ReactionButton
                targetType="comment"
                targetId={comment.id}
                initial={reaction}
                size="sm"
              />
            ) : null}
            {canReply(comment) ? (
              currentUserId ? (
                <button className={styles.action} onClick={() => setReplying((v) => !v)}>
                  REPLY
                </button>
              ) : null
            ) : (
              <span className={styles.maxDepth} title="Comments may only nest 5 levels deep.">
                REPLY · MAX DEPTH
              </span>
            )}
            {mayModify && isOwn ? (
              <button
                className={styles.action}
                onClick={() => {
                  setEditDraft(comment.body);
                  setEditing((v) => !v);
                }}
              >
                EDIT
              </button>
            ) : null}
            {mayModify ? (
              <button className={styles.actionDanger} onClick={() => void onDelete(comment.id)}>
                DELETE
              </button>
            ) : null}
          </div>

          {replying ? (
            <div className={styles.replyBox}>
              <MarkdownEditor
                value={draft}
                onChange={setDraft}
                rows={3}
                compact
                placeholder="Reply… Markdown & inline media supported."
                onUploadImage={onUploadImage}
              />
              <div className={styles.replyActions}>
                <Button size="sm" variant="ghost" onClick={() => setReplying(false)}>
                  Cancel
                </Button>
                <Button
                  size="sm"
                  onClick={() => void submitReply()}
                  disabled={!draft.trim()}
                  loading={busy}
                >
                  Reply
                </Button>
              </div>
            </div>
          ) : null}
        </div>
      </div>
    </div>
  );
}
