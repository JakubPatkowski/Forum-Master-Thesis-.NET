"use client";

/**
 * The whole comment area of a thread page: root composer (markdown + inline image
 * upload wired to the real Files flow), the path-ordered flat tree, batch like-count
 * hydration, and realtime NEW-glow bookkeeping (ids that arrived over the socket while
 * this thread was open get the cyan pulse instead of silently appearing).
 */

import Link from "next/link";
import { useEffect, useMemo, useRef, useState } from "react";

import { CommentNode } from "@/components/comments/CommentNode";
import { MarkdownEditor } from "@/components/compose/MarkdownEditor";
import { Button } from "@/components/ui/Button";
import { EmptyState } from "@/components/ui/EmptyState";
import { Skeleton } from "@/components/ui/Skeleton";
import { useToast } from "@/components/ui/toast";
import { filesApi } from "@/lib/api/files";
import { useAuth } from "@/lib/auth/auth-context";
import {
  useComments,
  useCreateComment,
  useDeleteComment,
  useUpdateComment,
} from "@/lib/hooks/use-content";
import { useReactionBatch } from "@/lib/hooks/use-reactions";
import { useRealtime } from "@/lib/realtime/realtime-context";
import { uploadFile } from "@/lib/upload/upload";

import styles from "./CommentSection.module.css";

export function CommentSection({
  threadId,
  threadOwnerId,
}: {
  threadId: string;
  threadOwnerId: string;
}) {
  const { currentUser, isModerator, isAuthenticated } = useAuth();
  const { addNotificationListener } = useRealtime();
  const { showError } = useToast();
  const comments = useComments(threadId);
  const createComment = useCreateComment(threadId);
  const updateComment = useUpdateComment(threadId);
  const deleteComment = useDeleteComment(threadId);

  const [draft, setDraft] = useState("");
  const [posting, setPosting] = useState(false);
  const [newIds, setNewIds] = useState<ReadonlySet<string>>(new Set());
  const ownCommentIds = useRef(new Set<string>());

  // Track comment ids that arrived via the realtime feed for this thread — those rows
  // render with the NEW badge + glow when the invalidated query re-fetches them.
  useEffect(
    () =>
      addNotificationListener((notification) => {
        if (
          notification.entity === "comment" &&
          notification.type === "created" &&
          notification.parentId === threadId &&
          !ownCommentIds.current.has(notification.id)
        ) {
          setNewIds((ids) => new Set([...ids, notification.id]));
        }
      }),
    [addNotificationListener, threadId],
  );

  const commentIds = useMemo(() => (comments.data ?? []).map((c) => c.id), [comments.data]);
  const reactions = useReactionBatch("comment", commentIds);

  const uploadInlineImage = async (file: File): Promise<string | null> => {
    try {
      const committed = await uploadFile(file, () => {});
      return committed.fileId;
    } catch (error) {
      showError(error);
      return null;
    }
  };

  const postRoot = async () => {
    if (!draft.trim()) return;
    setPosting(true);
    try {
      const { commentId } = await createComment.mutateAsync({ parentId: null, body: draft.trim() });
      ownCommentIds.current.add(commentId);
      // Inline images in comments live via the media convention; attach them so the
      // Files module tracks the comment as their target.
      await attachInlineImages(draft, commentId);
      setDraft("");
    } catch (error) {
      showError(error);
    } finally {
      setPosting(false);
    }
  };

  const attachInlineImages = async (body: string, commentId: string) => {
    const refs = [...body.matchAll(/!\[[^\]]*\]\(image:([0-9A-HJKMNP-TV-Z]{26})\)/gi)].map(
      (m) => m[1]!,
    );
    await Promise.all(
      refs.map((fileId) =>
        filesApi.attach(fileId, { targetType: "comment", targetId: commentId }).catch(() => {}),
      ),
    );
  };

  const onReply = async (parentId: string, body: string) => {
    try {
      const { commentId } = await createComment.mutateAsync({ parentId, body });
      ownCommentIds.current.add(commentId);
      await attachInlineImages(body, commentId);
    } catch (error) {
      showError(error);
      throw error;
    }
  };

  const onEdit = async (commentId: string, body: string) => {
    try {
      await updateComment.mutateAsync({ commentId, body });
      // An edit can introduce new inline images — attach them so Files tracks the comment
      // as their target (already-attached ones are idempotent no-ops server-side).
      await attachInlineImages(body, commentId);
    } catch (error) {
      showError(error);
      throw error;
    }
  };

  const onDelete = async (commentId: string) => {
    try {
      await deleteComment.mutateAsync(commentId);
    } catch (error) {
      showError(error);
    }
  };

  const list = comments.data ?? [];

  return (
    <section>
      <div className={styles.heading}>
        <h2 className={styles.title}>Comments</h2>
        {comments.data ? <span className={styles.count}>{list.length}</span> : null}
      </div>

      {isAuthenticated ? (
        <div className={styles.composer}>
          <MarkdownEditor
            value={draft}
            onChange={setDraft}
            rows={3}
            compact
            placeholder="Write a comment… Markdown supported. Insert images inline."
            onUploadImage={uploadInlineImage}
          />
          <div className={styles.composerFooter}>
            <span className={styles.composerNote}>IMAGES INLINE · MAX DEPTH 5</span>
            <Button
              size="sm"
              onClick={() => void postRoot()}
              disabled={!draft.trim()}
              loading={posting}
            >
              Post comment
            </Button>
          </div>
        </div>
      ) : (
        <div className={styles.anonNote}>
          <Link href="/auth">Log in to join the discussion →</Link>
        </div>
      )}

      {comments.isLoading ? (
        <div className={styles.skeletons}>
          <Skeleton height={90} />
          <Skeleton height={90} />
          <Skeleton height={90} />
        </div>
      ) : list.length === 0 ? (
        <EmptyState
          title="No comments yet"
          description="Be the first to reply — the thread author gets a realtime nudge."
        />
      ) : (
        <div className={styles.tree}>
          {list.map((comment) => (
            <CommentNode
              key={comment.id}
              comment={comment}
              threadOwnerId={threadOwnerId}
              currentUserId={currentUser?.id ?? null}
              isModerator={isModerator}
              reaction={reactions.data?.get(comment.id)}
              isNew={newIds.has(comment.id)}
              onReply={onReply}
              onEdit={onEdit}
              onDelete={onDelete}
              onUploadImage={uploadInlineImage}
            />
          ))}
        </div>
      )}
    </section>
  );
}
