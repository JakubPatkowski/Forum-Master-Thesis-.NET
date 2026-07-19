import { apiFetch } from "@/lib/api/http";
import type {
  BlockedUserResponse,
  ConversationResponse,
  CreateGroupRequest,
  CreateGroupResponse,
  CursorPage,
  FriendRequestsResponse,
  FriendResponse,
  GroupDetailResponse,
  GroupInviteResponse,
  GroupListFilter,
  GroupMemberResponse,
  GroupSummaryResponse,
  InviteToGroupResponse,
  MarkNotificationsReadResponse,
  MessageResponse,
  NotificationResponse,
  OpenDirectConversationResponse,
  PresenceEntry,
  PrivacySettingsResponse,
  SendFriendRequestResponse,
  SendMessageResponse,
  UnreadNotificationCountResponse,
  UpdateGroupRequest,
} from "@/lib/api/types";

function cursorQuery(cursor: string | null, limit?: number): string {
  const params = new URLSearchParams();
  if (cursor) params.set("cursor", cursor);
  if (limit !== undefined) params.set("limit", String(limit));
  const qs = params.toString();
  return qs ? `?${qs}` : "";
}

/**
 * The Social module surface (all endpoints require authentication). Verified against
 * the shipped backend routes (Forum.Modules.Social/Presentation, 2026-07-18). Keyset
 * cursors are plain last-row ULIDs passed back verbatim.
 */
export const socialApi = {
  // --- Friends ---------------------------------------------------------------

  sendFriendRequest: (addresseeId: string) =>
    apiFetch<SendFriendRequestResponse>("/api/social/friends/requests", {
      method: "POST",
      body: { addresseeId },
    }),

  acceptFriendRequest: (friendshipId: string) =>
    apiFetch(`/api/social/friends/requests/${friendshipId}/accept`, { method: "POST" }),

  /** Decline (as addressee) or cancel (as requester) — the row is deleted either way. */
  deleteFriendRequest: (friendshipId: string) =>
    apiFetch(`/api/social/friends/requests/${friendshipId}`, { method: "DELETE" }),

  removeFriend: (userId: string) =>
    apiFetch(`/api/social/friends/${userId}`, { method: "DELETE" }),

  getFriends: (cursor: string | null, limit?: number) =>
    apiFetch<CursorPage<FriendResponse>>(`/api/social/friends${cursorQuery(cursor, limit)}`),

  getFriendRequests: () => apiFetch<FriendRequestsResponse>("/api/social/friends/requests"),

  // --- Blocks ----------------------------------------------------------------

  /** Idempotent; creating a block also severs friendship + pending invites server-side. */
  blockUser: (userId: string) => apiFetch(`/api/social/blocks/${userId}`, { method: "PUT" }),

  unblockUser: (userId: string) => apiFetch(`/api/social/blocks/${userId}`, { method: "DELETE" }),

  getBlockedUsers: () => apiFetch<BlockedUserResponse[]>("/api/social/blocks"),

  // --- Groups ----------------------------------------------------------------

  createGroup: (request: CreateGroupRequest) =>
    apiFetch<CreateGroupResponse>("/api/social/groups", { method: "POST", body: request }),

  getGroups: (filter: GroupListFilter, cursor: string | null, limit?: number) => {
    const qs = cursorQuery(cursor, limit);
    return apiFetch<CursorPage<GroupSummaryResponse>>(
      `/api/social/groups${qs ? `${qs}&` : "?"}filter=${filter}`,
    );
  },

  getGroup: (groupId: string) => apiFetch<GroupDetailResponse>(`/api/social/groups/${groupId}`),

  updateGroup: (groupId: string, request: UpdateGroupRequest) =>
    apiFetch(`/api/social/groups/${groupId}`, { method: "PUT", body: request }),

  deleteGroup: (groupId: string) => apiFetch(`/api/social/groups/${groupId}`, { method: "DELETE" }),

  getGroupMembers: (groupId: string, cursor: string | null, limit?: number) =>
    apiFetch<CursorPage<GroupMemberResponse>>(
      `/api/social/groups/${groupId}/members${cursorQuery(cursor, limit)}`,
    ),

  /** Kick (owner/admin) — self-leave goes through leaveGroup, not here. */
  kickGroupMember: (groupId: string, userId: string) =>
    apiFetch(`/api/social/groups/${groupId}/members/${userId}`, { method: "DELETE" }),

  /** Public groups only; private groups are invite-only. */
  joinGroup: (groupId: string) => apiFetch(`/api/social/groups/${groupId}/join`, { method: "POST" }),

  /** The owner gets a 422 — they must transfer ownership or delete the group first. */
  leaveGroup: (groupId: string) =>
    apiFetch(`/api/social/groups/${groupId}/leave`, { method: "POST" }),

  setGroupMemberRole: (groupId: string, userId: string, role: "admin" | "member") =>
    apiFetch(`/api/social/groups/${groupId}/members/${userId}/role`, {
      method: "PUT",
      body: { role },
    }),

  /** The only way an owner can leave: hand the group to another member first. */
  transferGroupOwnership: (groupId: string, userId: string) =>
    apiFetch(`/api/social/groups/${groupId}/owner`, { method: "PUT", body: { userId } }),

  inviteToGroup: (groupId: string, userId: string) =>
    apiFetch<InviteToGroupResponse>(`/api/social/groups/${groupId}/invites`, {
      method: "POST",
      body: { userId },
    }),

  /** Pending invites addressed to ME (there is no sent-invites listing). */
  getMyInvites: () => apiFetch<GroupInviteResponse[]>("/api/social/invites"),

  acceptGroupInvite: (inviteId: string) =>
    apiFetch(`/api/social/invites/${inviteId}/accept`, { method: "POST" }),

  /** Decline (as invitee) or cancel (as inviter). */
  deleteGroupInvite: (inviteId: string) =>
    apiFetch(`/api/social/invites/${inviteId}`, { method: "DELETE" }),

  // --- Messaging -------------------------------------------------------------

  /** Get-or-create a DM (race-safe server-side); privacy/block gated per call. */
  openDirectConversation: (userId: string) =>
    apiFetch<OpenDirectConversationResponse>("/api/social/conversations/direct", {
      method: "POST",
      body: { userId },
    }),

  /** The one no-cursor list: unstable last-activity order, hard-capped at 200. */
  getConversations: (limit?: number) =>
    apiFetch<ConversationResponse[]>(
      `/api/social/conversations${limit !== undefined ? `?limit=${limit}` : ""}`,
    ),

  /** Newest-first keyset history; tombstoned rows arrive with body "[deleted]". */
  getMessages: (conversationId: string, cursor: string | null, limit?: number) =>
    apiFetch<CursorPage<MessageResponse>>(
      `/api/social/conversations/${conversationId}/messages${cursorQuery(cursor, limit)}`,
    ),

  sendMessage: (conversationId: string, body: string) =>
    apiFetch<SendMessageResponse>(`/api/social/conversations/${conversationId}/messages`, {
      method: "POST",
      body: { body },
    }),

  /** Sender only. */
  editMessage: (messageId: string, body: string) =>
    apiFetch(`/api/social/messages/${messageId}`, { method: "PUT", body: { body } }),

  /** Sender, or group owner/admin in GROUP chats only — tombstones to "[deleted]". */
  deleteMessage: (messageId: string) =>
    apiFetch(`/api/social/messages/${messageId}`, { method: "DELETE" }),

  /** Stamps MY last-read position (drives my unread badge; never a sender receipt). */
  markConversationRead: (conversationId: string) =>
    apiFetch(`/api/social/conversations/${conversationId}/read`, { method: "POST" }),

  // --- Notifications ---------------------------------------------------------

  getNotifications: (unreadOnly: boolean, cursor: string | null, limit?: number) => {
    const qs = cursorQuery(cursor, limit);
    return apiFetch<CursorPage<NotificationResponse>>(
      `/api/social/notifications${qs ? `${qs}&` : "?"}unreadOnly=${unreadOnly}`,
    );
  },

  /** Absent ids = mark ALL read. */
  markNotificationsRead: (ids?: string[]) =>
    apiFetch<MarkNotificationsReadResponse>("/api/social/notifications/read", {
      method: "POST",
      body: { ids: ids ?? null },
    }),

  getUnreadNotificationCount: () =>
    apiFetch<UnreadNotificationCountResponse>("/api/social/notifications/unread-count"),

  // --- Privacy ---------------------------------------------------------------

  getPrivacySettings: () => apiFetch<PrivacySettingsResponse>("/api/social/privacy"),

  updatePrivacySettings: (settings: PrivacySettingsResponse) =>
    apiFetch("/api/social/privacy", { method: "PUT", body: settings }),

  // --- Presence (poll-only — deliberately not on the realtime bus) -------------

  /** Batch lookup, ≤100 comma-separated ids (the reaction-batch precedent). */
  getPresence: (userIds: string[]) =>
    apiFetch<PresenceEntry[]>(`/api/social/presence?userIds=${userIds.join(",")}`),

  /** Beat every ~30 s while the tab is visible; missing two beats reads as away/offline. */
  heartbeat: () => apiFetch("/api/social/presence/heartbeat", { method: "POST" }),
};
