/**
 * Hand-authored types for the backend contract, verified against the backend source
 * (docs/architecture/FRONTEND-DESIGN-BRIEF.md §4 + backend/src DTO records).
 *
 * All ids are ULIDs serialized as 26-char strings; all timestamps are ISO-8601 strings.
 */

// ---------------------------------------------------------------------------
// Pagination — keyset/cursor only. `nextCursor` is opaque: pass it back verbatim,
// never parse or construct it. There is never a total count.
// ---------------------------------------------------------------------------

export interface CursorPage<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
}

// --- Identity ---------------------------------------------------------------

export interface RegisterRequest {
  username: string;
  email: string;
  displayName: string;
  password: string;
}

export interface RegisterResponse {
  userId: string;
}

export interface LoginRequest {
  email: string;
  password: string;
}

/** Login and refresh both return this; the refresh cookie is set/rotated out of band. */
export interface AccessTokenResponse {
  accessToken: string;
  expiresOnUtc: string;
}

// --- Content: categories -----------------------------------------------------

export type CategoryVisibility = "public" | "private";

export interface CategoryResponse {
  id: string;
  slug: string;
  name: string;
  description: string;
  visibility: CategoryVisibility;
  ownerId: string;
  /** Never set by any current endpoint — always null today (Phase 6+ wiring gap). */
  iconFileId: string | null;
  createdOnUtc: string;
}

export interface CreateCategoryRequest {
  slug: string;
  name: string;
  description?: string;
  visibility?: CategoryVisibility;
}

export interface CreateCategoryResponse {
  categoryId: string;
  slug: string;
}

export interface UpdateCategoryRequest {
  name: string;
  description?: string;
  visibility?: CategoryVisibility;
}

// --- Content: threads ---------------------------------------------------------

/**
 * Feed and search item. Gotcha: `likeCount` and `commentCount` are hard-coded 0 in SQL
 * today — real like counts come from the Engagement batch endpoint; comment counts have
 * no batch source at all, so the UI renders nothing for them (never a misleading "0").
 */
export interface ThreadFeedItemResponse {
  id: string;
  categoryId: string;
  categorySlug: string;
  categoryName: string;
  title: string;
  isPinned: boolean;
  ownerId: string;
  username: string;
  displayName: string;
  likeCount: number;
  commentCount: number;
  createdOnUtc: string;
  lastModifiedOnUtc: string | null;
}

export interface ThreadDetailResponse {
  id: string;
  categoryId: string;
  categorySlug: string;
  categoryName: string;
  title: string;
  /** Raw, unsanitized markdown — must go through the sanitizing renderer. */
  body: string;
  isPinned: boolean;
  ownerId: string;
  username: string;
  displayName: string;
  tags: string[];
  createdOnUtc: string;
  lastModifiedOnUtc: string | null;
}

export interface CreateThreadRequest {
  categoryId: string;
  title: string;
  body: string;
  /** Max 5, lowercase-kebab-case, ≤32 chars each; get-or-create server-side. */
  tagSlugs?: string[];
}

export interface CreateThreadResponse {
  threadId: string;
}

export interface UpdateThreadRequest {
  title: string;
  body: string;
}

// --- Content: comments --------------------------------------------------------

/**
 * Flat list ordered depth-first by `path`. Max depth is 5 (root = 0). A soft-deleted
 * comment keeps its place: body is the literal "[deleted]", `isDeleted: true`, children
 * stay nested and author fields are returned untouched.
 */
export interface CommentResponse {
  id: string;
  threadId: string;
  parentId: string | null;
  path: string;
  depth: number;
  body: string;
  isDeleted: boolean;
  ownerId: string;
  username: string;
  displayName: string;
  createdOnUtc: string;
}

export interface CreateCommentRequest {
  parentId: string | null;
  body: string;
}

export interface CreateCommentResponse {
  commentId: string;
}

export interface TagSuggestionResponse {
  slug: string;
  name: string;
  usageCount: number;
}

/** One row of a user's comment activity — the comment plus its live thread's title. */
export interface CommentActivityItemResponse {
  id: string;
  threadId: string;
  threadTitle: string;
  /** Raw markdown — render through the sanitizing renderer or as plain text. */
  body: string;
  createdOnUtc: string;
}

// --- Files ---------------------------------------------------------------------

export type FileTargetType =
  | "thread"
  | "comment"
  | "avatar"
  | "category_icon"
  | "thread_icon"
  | "dm";

export const ALLOWED_UPLOAD_TYPES = ["image/png", "image/jpeg", "image/gif", "image/webp"];
export const MAX_UPLOAD_BYTES = 5 * 1024 * 1024; // 5 MiB
export const MAX_ATTACHMENTS_PER_TARGET = 10;

export interface InitiateUploadRequest {
  contentType: string;
  sizeBytes: number;
}

export interface InitiateUploadResponse {
  fileId: string;
  objectKey: string;
  uploadUrl: string;
  method: string;
  expiresOnUtc: string;
}

/** Commit returns real, server-sniffed values — never the declared ones. */
export interface CommitUploadResponse {
  fileId: string;
  contentType: string;
  sizeBytes: number;
  width: number | null;
  height: number | null;
}

export interface FileDownloadResponse {
  fileId: string;
  url: string;
  contentType: string;
  sizeBytes: number;
  width: number | null;
  height: number | null;
  expiresOnUtc: string;
}

export interface AttachFileRequest {
  targetType: FileTargetType;
  targetId?: string;
}

// --- Engagement ------------------------------------------------------------------

export type ReactionTargetType = "thread" | "comment";

export interface ReactionSummaryResponse {
  targetId: string;
  count: number;
  viewerReacted: boolean;
}

export interface UserStatsResponse {
  userId: string;
  username: string;
  displayName: string;
  threadCount: number;
  commentCount: number;
  karma: number;
}

// --- Realtime ----------------------------------------------------------------------

export interface RealtimeTicketResponse {
  ticket: string;
  expiresInSeconds: number;
}

export type RealtimeViewKind = "category" | "thread" | "user";

export interface RealtimeClientMessage {
  action: "subscribe" | "unsubscribe";
  view: RealtimeViewKind;
  id: string;
}

/** Server ack / error frames (distinguished from notifications by having no `entity`). */
export interface RealtimeControlMessage {
  type: "subscribed" | "unsubscribed" | "error";
  view?: string;
  id?: string;
  reason?:
    | "unknown-view"
    | "unknown-action"
    | "malformed-message"
    | "forbidden-view"
    | "too-many-subscriptions";
}

/**
 * Change notification — carries no content, ever. The only correct reaction is to
 * re-fetch the affected resource (fetch-then-patch, ADR 0010).
 * entity=thread: id=thread, parentId=null. entity=comment: id=comment, parentId=thread.
 * entity=reaction: id=the reacted-to thread/comment, parentId=containing thread
 * (null when the target is itself a thread).
 */
export interface ChangeNotification {
  type: "created" | "updated" | "deleted";
  entity: "thread" | "comment" | "reaction";
  id: string;
  parentId?: string | null;
  categoryId?: string | null;
}

export type RealtimeServerMessage = RealtimeControlMessage | ChangeNotification;

export function isChangeNotification(msg: RealtimeServerMessage): msg is ChangeNotification {
  return "entity" in msg && typeof msg.entity === "string";
}
