import { apiFetch } from "@/lib/api/http";
import type {
  CategoryResponse,
  CommentActivityItemResponse,
  CommentResponse,
  CreateCategoryRequest,
  CreateCategoryResponse,
  CreateCommentRequest,
  CreateCommentResponse,
  CreateThreadRequest,
  CreateThreadResponse,
  CursorPage,
  TagSuggestionResponse,
  ThreadDetailResponse,
  ThreadFeedItemResponse,
  UpdateCategoryRequest,
  UpdateThreadRequest,
} from "@/lib/api/types";

function pageParams(cursor: string | null | undefined, limit: number): string {
  const params = new URLSearchParams();
  if (cursor) params.set("cursor", cursor);
  params.set("limit", String(limit));
  return params.toString();
}

export const contentApi = {
  // --- categories ---
  listCategories: () => apiFetch<CategoryResponse[]>("/api/content/categories"),

  getCategory: (slug: string) =>
    apiFetch<CategoryResponse>(`/api/content/categories/${encodeURIComponent(slug)}`),

  createCategory: (request: CreateCategoryRequest) =>
    apiFetch<CreateCategoryResponse>("/api/content/categories", {
      method: "POST",
      body: request,
    }),

  updateCategory: (slug: string, request: UpdateCategoryRequest) =>
    apiFetch(`/api/content/categories/${encodeURIComponent(slug)}`, {
      method: "PUT",
      body: request,
    }),

  deleteCategory: (slug: string) =>
    apiFetch(`/api/content/categories/${encodeURIComponent(slug)}`, { method: "DELETE" }),

  // --- threads ---
  /** categoryId is REQUIRED by the backend — there is no global feed endpoint. */
  getThreadFeed: (categoryId: string, cursor?: string | null, limit = 20) =>
    apiFetch<CursorPage<ThreadFeedItemResponse>>(
      `/api/content/threads?categoryId=${categoryId}&${pageParams(cursor, limit)}`,
    ),

  getThread: (threadId: string) =>
    apiFetch<ThreadDetailResponse>(`/api/content/threads/${threadId}`),

  createThread: (request: CreateThreadRequest) =>
    apiFetch<CreateThreadResponse>("/api/content/threads", { method: "POST", body: request }),

  updateThread: (threadId: string, request: UpdateThreadRequest) =>
    apiFetch(`/api/content/threads/${threadId}`, { method: "PUT", body: request }),

  deleteThread: (threadId: string) =>
    apiFetch(`/api/content/threads/${threadId}`, { method: "DELETE" }),

  /** Moderator of the CURRENT category only — an owner alone cannot move a thread. */
  changeThreadCategory: (threadId: string, categoryId: string) =>
    apiFetch(`/api/content/threads/${threadId}/category`, {
      method: "PATCH",
      body: { categoryId },
    }),

  /** Moderator of the category only. */
  pinThread: (threadId: string, pinned: boolean) =>
    apiFetch(`/api/content/threads/${threadId}/pin`, { method: "POST", body: { pinned } }),

  searchThreads: (q: string, cursor?: string | null, limit = 20) =>
    apiFetch<CursorPage<ThreadFeedItemResponse>>(
      `/api/content/search?q=${encodeURIComponent(q)}&${pageParams(cursor, limit)}`,
    ),

  // --- user activity (profile timeline) ---
  getUserThreads: (userId: string, cursor?: string | null, limit = 20) =>
    apiFetch<CursorPage<ThreadFeedItemResponse>>(
      `/api/content/users/${userId}/threads?${pageParams(cursor, limit)}`,
    ),

  getUserComments: (userId: string, cursor?: string | null, limit = 20) =>
    apiFetch<CursorPage<CommentActivityItemResponse>>(
      `/api/content/users/${userId}/comments?${pageParams(cursor, limit)}`,
    ),

  // --- tags ---
  /** Popularity-ranked when query is empty; substring match on slug otherwise. */
  suggestTags: (query: string, limit = 20) =>
    apiFetch<TagSuggestionResponse[]>(
      `/api/content/tags?${query ? `query=${encodeURIComponent(query)}&` : ""}limit=${limit}`,
    ),

  // --- comments ---
  getCommentTree: (threadId: string) =>
    apiFetch<CommentResponse[]>(`/api/content/threads/${threadId}/comments`),

  createComment: (threadId: string, request: CreateCommentRequest) =>
    apiFetch<CreateCommentResponse>(`/api/content/threads/${threadId}/comments`, {
      method: "POST",
      body: request,
    }),

  updateComment: (commentId: string, body: string) =>
    apiFetch(`/api/content/comments/${commentId}`, { method: "PUT", body: { body } }),

  deleteComment: (commentId: string) =>
    apiFetch(`/api/content/comments/${commentId}`, { method: "DELETE" }),
};
