/**
 * Comment-tree helpers. The API already returns the tree as a FLAT list ordered
 * depth-first by materialized `path` — rendering is a straight map over that order with
 * per-node indentation; no client-side tree building is needed (or wanted: re-sorting
 * could disagree with the server's path order).
 */

import type { CommentResponse } from "@/lib/api/types";

/** Root = depth 0; replies may go to depth 1–5. Replying AT depth 5 is a 422. */
export const MAX_COMMENT_DEPTH = 5;

export function canReply(comment: Pick<CommentResponse, "depth">): boolean {
  return comment.depth < MAX_COMMENT_DEPTH;
}

/** A soft-deleted comment stays in place with body "[deleted]"; children remain nested. */
export function isTombstone(comment: Pick<CommentResponse, "isDeleted">): boolean {
  return comment.isDeleted;
}

/**
 * Visual indent per depth, capped so depth-5 chains stay readable on narrow viewports
 * (the cap is the responsive-degradation technique chosen for nested trees).
 */
export function indentPx(depth: number, step = 26, maxSteps = 5): number {
  return Math.min(depth, maxSteps) * step;
}

export function countVisible(comments: readonly CommentResponse[]): number {
  return comments.length;
}
