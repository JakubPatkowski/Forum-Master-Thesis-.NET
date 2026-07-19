"use client";

/**
 * Server-state hooks for the Social module. Every list here except presence is
 * realtime-covered (lib/realtime/invalidation.ts patches them from WS pushes), so
 * queries use staleTimes.realtimeCovered exactly like threads/comments/reactions do.
 * Mutations invalidate the same key roots the push feed targets — the WS echo of our
 * own action then finds an already-fresh cache and no double fetch happens.
 */

import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { queryKeys } from "@/lib/api/keys";
import { socialApi } from "@/lib/api/social";
import { staleTimes } from "@/lib/api/stale-times";
import type {
  CreateGroupRequest,
  GroupListFilter,
  PrivacySettingsResponse,
  UpdateGroupRequest,
} from "@/lib/api/types";
import { useAuth } from "@/lib/auth/auth-context";

// --- Friends -----------------------------------------------------------------

export function useFriends(limit = 50) {
  const { isAuthenticated } = useAuth();
  return useInfiniteQuery({
    queryKey: queryKeys.friends,
    queryFn: ({ pageParam }) => socialApi.getFriends(pageParam, limit),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.nextCursor : null),
    enabled: isAuthenticated,
    staleTime: staleTimes.realtimeCovered,
  });
}

export function useFriendRequests() {
  const { isAuthenticated } = useAuth();
  return useQuery({
    queryKey: queryKeys.friendRequests,
    queryFn: () => socialApi.getFriendRequests(),
    enabled: isAuthenticated,
    staleTime: staleTimes.realtimeCovered,
  });
}

export function useSendFriendRequest() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (addresseeId: string) => socialApi.sendFriendRequest(addresseeId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.friendRequests });
    },
  });
}

export function useAcceptFriendRequest() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (friendshipId: string) => socialApi.acceptFriendRequest(friendshipId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.friendRequests });
      void queryClient.invalidateQueries({ queryKey: queryKeys.friends });
    },
  });
}

/** Decline (addressee) or cancel (requester) — same endpoint, row deleted either way. */
export function useDeleteFriendRequest() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (friendshipId: string) => socialApi.deleteFriendRequest(friendshipId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.friendRequests });
    },
  });
}

export function useRemoveFriend() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (userId: string) => socialApi.removeFriend(userId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.friends });
    },
  });
}

// --- Blocks ------------------------------------------------------------------

export function useBlockedUsers() {
  const { isAuthenticated } = useAuth();
  return useQuery({
    queryKey: queryKeys.blocks,
    queryFn: () => socialApi.getBlockedUsers(),
    enabled: isAuthenticated,
    // Blocks are mutation-only from this client's perspective (no realtime coverage).
    staleTime: staleTimes.reference,
  });
}

export function useBlockUser() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (userId: string) => socialApi.blockUser(userId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.blocks });
      // A new block severs friendship + pending invites server-side.
      void queryClient.invalidateQueries({ queryKey: queryKeys.friends });
      void queryClient.invalidateQueries({ queryKey: queryKeys.friendRequests });
      void queryClient.invalidateQueries({ queryKey: queryKeys.groupInvites });
    },
  });
}

export function useUnblockUser() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (userId: string) => socialApi.unblockUser(userId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.blocks });
    },
  });
}

// --- Groups ------------------------------------------------------------------

export function useGroups(filter: GroupListFilter, limit = 30) {
  const { isAuthenticated } = useAuth();
  return useInfiniteQuery({
    queryKey: queryKeys.groups(filter),
    queryFn: ({ pageParam }) => socialApi.getGroups(filter, pageParam, limit),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.nextCursor : null),
    enabled: isAuthenticated,
    staleTime: staleTimes.realtimeCovered,
  });
}

export function useGroup(groupId: string | null) {
  const { isAuthenticated } = useAuth();
  return useQuery({
    queryKey: queryKeys.group(groupId ?? "none"),
    queryFn: () => socialApi.getGroup(groupId!),
    enabled: isAuthenticated && groupId !== null,
    staleTime: staleTimes.realtimeCovered,
  });
}

export function useGroupMembers(groupId: string | null, limit = 50) {
  const { isAuthenticated } = useAuth();
  return useInfiniteQuery({
    queryKey: queryKeys.groupMembers(groupId ?? "none"),
    queryFn: ({ pageParam }) => socialApi.getGroupMembers(groupId!, pageParam, limit),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.nextCursor : null),
    enabled: isAuthenticated && groupId !== null,
    staleTime: staleTimes.realtimeCovered,
  });
}

export function useMyInvites() {
  const { isAuthenticated } = useAuth();
  return useQuery({
    queryKey: queryKeys.groupInvites,
    queryFn: () => socialApi.getMyInvites(),
    enabled: isAuthenticated,
    staleTime: staleTimes.realtimeCovered,
  });
}

function useInvalidateGroups() {
  const queryClient = useQueryClient();
  // ["groups"] prefixes both the lists and every detail entry.
  return () => void queryClient.invalidateQueries({ queryKey: ["groups"] });
}

export function useCreateGroup() {
  const invalidateGroups = useInvalidateGroups();
  return useMutation({
    mutationFn: (request: CreateGroupRequest) => socialApi.createGroup(request),
    onSuccess: invalidateGroups,
  });
}

export function useUpdateGroup(groupId: string) {
  const invalidateGroups = useInvalidateGroups();
  return useMutation({
    mutationFn: (request: UpdateGroupRequest) => socialApi.updateGroup(groupId, request),
    onSuccess: invalidateGroups,
  });
}

export function useDeleteGroup() {
  const queryClient = useQueryClient();
  const invalidateGroups = useInvalidateGroups();
  return useMutation({
    mutationFn: (groupId: string) => socialApi.deleteGroup(groupId),
    onSuccess: () => {
      invalidateGroups();
      // The group's chat disappears with it.
      void queryClient.invalidateQueries({ queryKey: queryKeys.conversations });
    },
  });
}

export function useJoinGroup() {
  const queryClient = useQueryClient();
  const invalidateGroups = useInvalidateGroups();
  return useMutation({
    mutationFn: (groupId: string) => socialApi.joinGroup(groupId),
    onSuccess: (_data, groupId) => {
      invalidateGroups();
      void queryClient.invalidateQueries({ queryKey: queryKeys.groupMembers(groupId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.conversations });
    },
  });
}

export function useLeaveGroup() {
  const queryClient = useQueryClient();
  const invalidateGroups = useInvalidateGroups();
  return useMutation({
    mutationFn: (groupId: string) => socialApi.leaveGroup(groupId),
    onSuccess: (_data, groupId) => {
      invalidateGroups();
      void queryClient.invalidateQueries({ queryKey: queryKeys.groupMembers(groupId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.conversations });
    },
  });
}

export function useKickGroupMember(groupId: string) {
  const queryClient = useQueryClient();
  const invalidateGroups = useInvalidateGroups();
  return useMutation({
    mutationFn: (userId: string) => socialApi.kickGroupMember(groupId, userId),
    onSuccess: () => {
      invalidateGroups();
      void queryClient.invalidateQueries({ queryKey: queryKeys.groupMembers(groupId) });
    },
  });
}

export function useSetGroupMemberRole(groupId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: "admin" | "member" }) =>
      socialApi.setGroupMemberRole(groupId, userId, role),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.groupMembers(groupId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.group(groupId) });
    },
  });
}

export function useTransferGroupOwnership(groupId: string) {
  const queryClient = useQueryClient();
  const invalidateGroups = useInvalidateGroups();
  return useMutation({
    mutationFn: (userId: string) => socialApi.transferGroupOwnership(groupId, userId),
    onSuccess: () => {
      invalidateGroups();
      void queryClient.invalidateQueries({ queryKey: queryKeys.groupMembers(groupId) });
    },
  });
}

export function useInviteToGroup(groupId: string) {
  return useMutation({
    mutationFn: (userId: string) => socialApi.inviteToGroup(groupId, userId),
    // Nothing to invalidate for the inviter — there is no sent-invites listing.
  });
}

export function useAcceptGroupInvite() {
  const queryClient = useQueryClient();
  const invalidateGroups = useInvalidateGroups();
  return useMutation({
    mutationFn: (inviteId: string) => socialApi.acceptGroupInvite(inviteId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.groupInvites });
      invalidateGroups();
      void queryClient.invalidateQueries({ queryKey: queryKeys.conversations });
    },
  });
}

export function useDeclineGroupInvite() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (inviteId: string) => socialApi.deleteGroupInvite(inviteId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.groupInvites });
    },
  });
}

// --- Messaging ---------------------------------------------------------------

export function useConversations() {
  const { isAuthenticated } = useAuth();
  return useQuery({
    queryKey: queryKeys.conversations,
    queryFn: () => socialApi.getConversations(),
    enabled: isAuthenticated,
    staleTime: staleTimes.realtimeCovered,
  });
}

export function useOpenDirectConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (userId: string) => socialApi.openDirectConversation(userId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.conversations });
    },
  });
}

/** Newest-first keyset pages; render reversed with LOAD OLDER ↑ at the top. */
export function useMessages(conversationId: string, limit = 30) {
  const { isAuthenticated } = useAuth();
  return useInfiniteQuery({
    queryKey: queryKeys.messages(conversationId),
    queryFn: ({ pageParam }) => socialApi.getMessages(conversationId, pageParam, limit),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.nextCursor : null),
    enabled: isAuthenticated,
    staleTime: staleTimes.realtimeCovered,
  });
}

export function useSendMessage(conversationId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: string) => socialApi.sendMessage(conversationId, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.messages(conversationId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.conversations });
    },
  });
}

export function useEditMessage(conversationId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ messageId, body }: { messageId: string; body: string }) =>
      socialApi.editMessage(messageId, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.messages(conversationId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.conversations });
    },
  });
}

export function useDeleteMessage(conversationId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (messageId: string) => socialApi.deleteMessage(messageId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.messages(conversationId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.conversations });
    },
  });
}

export function useMarkConversationRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (conversationId: string) => socialApi.markConversationRead(conversationId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.conversations });
    },
  });
}

// --- Notifications -----------------------------------------------------------

export function useNotifications(unreadOnly = false, limit = 20) {
  const { isAuthenticated } = useAuth();
  return useInfiniteQuery({
    queryKey: queryKeys.notifications(unreadOnly),
    queryFn: ({ pageParam }) => socialApi.getNotifications(unreadOnly, pageParam, limit),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.nextCursor : null),
    enabled: isAuthenticated,
    staleTime: staleTimes.realtimeCovered,
  });
}

/** The TopNav friends-bell badge source (independent from the messages badge). */
export function useUnreadNotificationCount() {
  const { isAuthenticated } = useAuth();
  return useQuery({
    queryKey: queryKeys.notificationUnreadCount,
    queryFn: () => socialApi.getUnreadNotificationCount(),
    enabled: isAuthenticated,
    staleTime: staleTimes.realtimeCovered,
  });
}

export function useMarkNotificationsRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (ids?: string[]) => socialApi.markNotificationsRead(ids),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["notifications"] });
    },
  });
}

// --- Privacy -----------------------------------------------------------------

export function usePrivacySettings() {
  const { isAuthenticated } = useAuth();
  return useQuery({
    queryKey: queryKeys.privacy,
    queryFn: () => socialApi.getPrivacySettings(),
    enabled: isAuthenticated,
    staleTime: staleTimes.reference,
  });
}

export function useUpdatePrivacySettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (settings: PrivacySettingsResponse) => socialApi.updatePrivacySettings(settings),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.privacy });
    },
  });
}
