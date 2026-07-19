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
  | "message"
  | "group_icon";

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

// --- Social ------------------------------------------------------------------------
// Verified against the shipped backend source (Forum.Modules.Social, 2026-07-18):
// ISocialQueries.cs read DTOs + the Presentation request records. All /api/social/*
// endpoints require authentication; keyset cursors are the plain ULID of the last row
// (pass back verbatim). The conversation list is the ONE no-cursor list (unstable
// last-activity order, hard-capped at 200 server-side).

export type GroupVisibility = "public" | "private";
export type ConversationType = "direct" | "group";
export type PrivacyAudience = "everyone" | "friends" | "no_one";
export type PresenceStatus = "online" | "away" | "offline";
/** Which groups a directory listing covers (GET /api/social/groups?filter=). */
export type GroupListFilter = "all" | "mine" | "public";

export interface FriendResponse {
  friendshipId: string;
  userId: string;
  username: string;
  friendsSinceUtc: string;
}

export interface FriendRequestResponse {
  friendshipId: string;
  requesterId: string;
  requesterUsername: string;
  addresseeId: string;
  addresseeUsername: string;
  sentOnUtc: string;
}

export interface FriendRequestsResponse {
  incoming: FriendRequestResponse[];
  outgoing: FriendRequestResponse[];
}

export interface SendFriendRequestResponse {
  friendshipId: string;
}

export interface BlockedUserResponse {
  userId: string;
  username: string;
  blockedOnUtc: string;
}

/**
 * Group visibility affects DISCOVERY/JOIN only — members/chat of a private group are
 * exactly as member-only as a public group's. Don't gate any UI on it beyond the
 * groups list and the join button.
 */
export interface GroupSummaryResponse {
  groupId: string;
  name: string;
  description: string;
  visibility: GroupVisibility;
  ownerId: string;
  ownerUsername: string;
  memberCount: number;
  isMember: boolean;
  createdOnUtc: string;
}

/** `isAdmin` already resolves owner-OR-promoted-admin — use it directly, never re-derive. */
export interface GroupDetailResponse extends GroupSummaryResponse {
  isAdmin: boolean;
}

export interface GroupMemberResponse {
  userId: string;
  username: string;
  joinedOnUtc: string;
  isOwner: boolean;
  isAdmin: boolean;
}

export interface GroupInviteResponse {
  inviteId: string;
  groupId: string;
  groupName: string;
  invitedUserId: string;
  invitedUserUsername: string;
  invitedBy: string;
  invitedByUsername: string;
  sentOnUtc: string;
}

export interface CreateGroupRequest {
  name: string;
  description?: string;
  visibility?: GroupVisibility;
}

export interface CreateGroupResponse {
  groupId: string;
}

export interface UpdateGroupRequest {
  name: string;
  description?: string;
  visibility?: GroupVisibility;
}

export interface InviteToGroupResponse {
  inviteId: string;
}

/** A group chat's conversation id IS the group id (backend invariant). */
export interface ConversationResponse {
  conversationId: string;
  type: ConversationType;
  displayName: string;
  otherUserId: string | null;
  groupId: string | null;
  lastMessageId: string | null;
  lastMessagePreview: string | null;
  lastMessageSenderId: string | null;
  lastMessageOnUtc: string | null;
  /** MY unread count for this seat — never a receipt visible to the sender. */
  unreadCount: number;
  isMuted: boolean;
}

export interface OpenDirectConversationResponse {
  conversationId: string;
}

/** A deleted message arrives with body already tombstoned to the literal "[deleted]". */
export interface MessageResponse {
  messageId: string;
  conversationId: string;
  senderId: string;
  senderUsername: string;
  body: string;
  sentOnUtc: string;
  editedOnUtc: string | null;
  isDeleted: boolean;
}

export interface SendMessageResponse {
  messageId: string;
  sentOnUtc: string;
}

export const MAX_MESSAGE_LENGTH = 4000;

/**
 * Closed kind set — message arrivals NEVER create a notification row; the messages
 * badge is a separate number (sum of ConversationResponse.unreadCount).
 * targetId per kind: friend.request/friend.accepted = friendshipId,
 * group.invite = inviteId, group.invite.accepted/group.kicked = groupId.
 */
export type NotificationKind =
  | "friend.request"
  | "friend.accepted"
  | "group.invite"
  | "group.invite.accepted"
  | "group.kicked";

export interface NotificationResponse {
  notificationId: string;
  kind: NotificationKind;
  actorId: string | null;
  actorUsername: string | null;
  targetId: string | null;
  isRead: boolean;
  createdOnUtc: string;
}

export interface UnreadNotificationCountResponse {
  unread: number;
}

export interface MarkNotificationsReadResponse {
  marked: number;
}

/**
 * `friendRequests: "friends"` is meaningless (friends need no request) — the backend
 * normalizes it to "no_one" on write, so the UI never offers it for that field.
 */
export interface PrivacySettingsResponse {
  friendRequests: PrivacyAudience;
  messages: PrivacyAudience;
  groupInvites: PrivacyAudience;
  showOnlineStatus: boolean;
}

/** Poll-only — presence is deliberately NOT on the realtime bus (backend design). */
export interface PresenceEntry {
  userId: string;
  status: PresenceStatus;
}

// --- Realtime ----------------------------------------------------------------------

export interface RealtimeTicketResponse {
  ticket: string;
  expiresInSeconds: number;
}

export type RealtimeViewKind = "category" | "thread" | "user" | "group" | "conversation";

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
 *
 * Social entities (verified against RealtimeEventMap.cs) always carry categoryId=null;
 * parentId is the container: the conversation for message, the group for group /
 * group_member / group_invite, absent for friendship / notification. For group_member
 * the id is the member's USER id (not a membership row id).
 */
export interface ChangeNotification {
  type: "created" | "updated" | "deleted";
  entity:
    | "thread"
    | "comment"
    | "reaction"
    | "friendship"
    | "group"
    | "group_member"
    | "group_invite"
    | "message"
    | "notification";
  id: string;
  parentId?: string | null;
  categoryId?: string | null;
}

export type RealtimeServerMessage = RealtimeControlMessage | ChangeNotification;

export function isChangeNotification(msg: RealtimeServerMessage): msg is ChangeNotification {
  return "entity" in msg && typeof msg.entity === "string";
}
