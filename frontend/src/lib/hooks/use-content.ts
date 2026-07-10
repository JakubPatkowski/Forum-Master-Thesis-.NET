"use client";

/** Server-state hooks for the Content module (categories, threads, comments, search). */

import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { contentApi } from "@/lib/api/content";
import { queryKeys } from "@/lib/api/keys";
import type {
  CreateCategoryRequest,
  CreateCommentRequest,
  CreateThreadRequest,
  UpdateCategoryRequest,
  UpdateThreadRequest,
} from "@/lib/api/types";

export function useCategories() {
  return useQuery({
    queryKey: queryKeys.categories,
    queryFn: () => contentApi.listCategories(),
    staleTime: 60_000,
  });
}

export function useCategory(slug: string) {
  return useQuery({
    queryKey: queryKeys.category(slug),
    queryFn: () => contentApi.getCategory(slug),
  });
}

/** Requires the global `create` permission (the same bit as posting a thread); the creator becomes owner. */
export function useCreateCategory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateCategoryRequest) => contentApi.createCategory(request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.categories });
    },
  });
}

/** Owner or moderator only (the backend enforces it; the UI gates the button as a hint). */
export function useUpdateCategory(slug: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: UpdateCategoryRequest) => contentApi.updateCategory(slug, request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.categories });
      void queryClient.invalidateQueries({ queryKey: queryKeys.category(slug) });
    },
  });
}

/** Keyset-paged category feed: nextCursor goes back verbatim as the next pageParam. */
export function useThreadFeed(categoryId: string | undefined, limit = 20) {
  return useInfiniteQuery({
    queryKey: queryKeys.threadFeed(categoryId ?? "unknown"),
    queryFn: ({ pageParam }) => contentApi.getThreadFeed(categoryId!, pageParam, limit),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.nextCursor : null),
    enabled: categoryId !== undefined,
  });
}

export function useThread(threadId: string) {
  return useQuery({
    queryKey: queryKeys.thread(threadId),
    queryFn: () => contentApi.getThread(threadId),
  });
}

export function useSearchThreads(q: string, limit = 20) {
  return useInfiniteQuery({
    queryKey: queryKeys.search(q),
    queryFn: ({ pageParam }) => contentApi.searchThreads(q, pageParam, limit),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.nextCursor : null),
    enabled: q.trim().length > 0,
  });
}

/** A user's live threads, newest first (profile activity). */
export function useUserThreads(userId: string, limit = 20) {
  return useInfiniteQuery({
    queryKey: queryKeys.userThreads(userId),
    queryFn: ({ pageParam }) => contentApi.getUserThreads(userId, pageParam, limit),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.nextCursor : null),
  });
}

/** A user's live comments on live threads, newest first (profile activity). */
export function useUserComments(userId: string, limit = 20) {
  return useInfiniteQuery({
    queryKey: queryKeys.userComments(userId),
    queryFn: ({ pageParam }) => contentApi.getUserComments(userId, pageParam, limit),
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => (lastPage.hasMore ? lastPage.nextCursor : null),
  });
}

export function useComments(threadId: string) {
  return useQuery({
    queryKey: queryKeys.comments(threadId),
    queryFn: () => contentApi.getCommentTree(threadId),
  });
}

export function useCreateThread() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateThreadRequest) => contentApi.createThread(request),
    onSuccess: (_data, request) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.threadFeed(request.categoryId) });
    },
  });
}

export function useUpdateThread(threadId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: UpdateThreadRequest) => contentApi.updateThread(threadId, request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.thread(threadId) });
    },
  });
}

export function useDeleteThread() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (threadId: string) => contentApi.deleteThread(threadId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["threads"] });
    },
  });
}

export function usePinThread(categoryId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ threadId, pinned }: { threadId: string; pinned: boolean }) =>
      contentApi.pinThread(threadId, pinned),
    onSuccess: (_data, { threadId }) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.threadFeed(categoryId) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.thread(threadId) });
    },
  });
}

export function useCreateComment(threadId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateCommentRequest) => contentApi.createComment(threadId, request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.comments(threadId) });
    },
  });
}

export function useUpdateComment(threadId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ commentId, body }: { commentId: string; body: string }) =>
      contentApi.updateComment(commentId, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.comments(threadId) });
    },
  });
}

export function useDeleteComment(threadId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (commentId: string) => contentApi.deleteComment(commentId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.comments(threadId) });
    },
  });
}
