// Thin HTTP helpers for the k6 scenarios (Phase 9c). Everything is tagged with { endpoint } so the
// summary (and the thesis tables) break latency out per logical operation, and every helper returns
// the raw k6 response — main.js owns checks and metrics.

import http from 'k6/http';

/** Every request carries the endpoint tag; JSON bodies get the SPA's Content-Type. */
function params(endpoint, token, extra = {}) {
  const headers = { Accept: 'application/json', ...(extra.headers || {}) };
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }
  return { ...extra, headers, tags: { endpoint } };
}

export function apiGet(baseUrl, path, endpoint, token = null) {
  return http.get(`${baseUrl}${path}`, params(endpoint, token));
}

export function apiPost(baseUrl, path, body, endpoint, token = null) {
  return http.post(`${baseUrl}${path}`, body === null ? null : JSON.stringify(body),
    params(endpoint, token, { headers: body === null ? {} : { 'Content-Type': 'application/json' } }));
}

export function apiPut(baseUrl, path, body, endpoint, token = null) {
  return http.put(`${baseUrl}${path}`, body === null ? null : JSON.stringify(body),
    params(endpoint, token, { headers: body === null ? {} : { 'Content-Type': 'application/json' } }));
}

export function apiDelete(baseUrl, path, endpoint, token = null) {
  return http.del(`${baseUrl}${path}`, null, params(endpoint, token));
}

/**
 * POST /api/identity/login → accessToken, or null on failure. Callers stagger this (setup-only):
 * each login burns ~400 ms of Argon2id CPU on the backend and the Auth limiter is deliberately tight.
 */
export function login(baseUrl, email, password) {
  const res = apiPost(baseUrl, '/api/identity/login', { email, password }, 'login');
  if (res.status !== 200) {
    return { token: null, status: res.status };
  }
  return { token: res.json('accessToken'), status: res.status };
}

/** GET /api/content/categories → [{id, slug, visibility, ...}] (anonymous). */
export function listCategories(baseUrl) {
  return apiGet(baseUrl, '/api/content/categories', 'categories');
}

/** Category feed page (keyset). cursor null → first page. */
export function threadFeed(baseUrl, categoryId, cursor = null, limit = 20) {
  const cursorPart = cursor ? `&cursor=${encodeURIComponent(cursor)}` : '';
  return apiGet(baseUrl, `/api/content/threads?categoryId=${categoryId}&limit=${limit}${cursorPart}`, 'feed');
}

/** The SPA's real thread-open pattern is 3 calls: detail + comment tree + reaction batch (G22 parity). */
export function threadDetail(baseUrl, threadId) {
  return apiGet(baseUrl, `/api/content/threads/${threadId}`, 'thread');
}

export function commentTree(baseUrl, threadId) {
  return apiGet(baseUrl, `/api/content/threads/${threadId}/comments`, 'comments');
}

export function reactionBatch(baseUrl, targetType, targetIds) {
  const ids = encodeURIComponent(targetIds.join(','));
  return apiGet(baseUrl, `/api/engagement/reactions/batch?targetType=${targetType}&targetIds=${ids}`, 'reactions_batch');
}

export function searchThreads(baseUrl, query) {
  return apiGet(baseUrl, `/api/content/search?q=${encodeURIComponent(query)}&limit=20`, 'search');
}

export function suggestTags(baseUrl, prefix) {
  return apiGet(baseUrl, `/api/content/tags?query=${encodeURIComponent(prefix)}&limit=20`, 'tags');
}

export function createComment(baseUrl, token, threadId, body) {
  return apiPost(baseUrl, `/api/content/threads/${threadId}/comments`, { parentId: null, body }, 'comment_create', token);
}

export function createThread(baseUrl, token, categoryId, title, body, tagSlugs) {
  return apiPost(baseUrl, '/api/content/threads', { categoryId, title, body, tagSlugs }, 'thread_create', token);
}

/** Idempotent both directions (200 no-op) — a pure toggle needs no local like-state bookkeeping. */
export function addReaction(baseUrl, token, threadId) {
  return apiPut(baseUrl, `/api/engagement/reactions/thread/${threadId}`, null, 'reaction_add', token);
}

export function removeReaction(baseUrl, token, threadId) {
  return apiDelete(baseUrl, `/api/engagement/reactions/thread/${threadId}`, 'reaction_remove', token);
}

export function userStats(baseUrl, userId) {
  return apiGet(baseUrl, `/api/engagement/users/${userId}/stats`, 'user_stats');
}

export function userThreads(baseUrl, userId) {
  return apiGet(baseUrl, `/api/content/users/${userId}/threads`, 'user_threads');
}

/** ADR 0008 golden path, step 1: declare type+size, receive presigned PUT URL. */
export function initiateUpload(baseUrl, token, contentType, sizeBytes) {
  return apiPost(baseUrl, '/api/files', { contentType, sizeBytes }, 'file_initiate', token);
}

/** Step 2: bytes go straight to MinIO (through the minio.forum.local ingress) — never the backend. */
export function uploadToPresignedUrl(url, bodyBuffer, contentType) {
  return http.put(url, bodyBuffer, { headers: { 'Content-Type': contentType }, tags: { endpoint: 'file_put' } });
}

/** Step 3: commit — backend stats the real object + sniffs magic bytes; mismatch would 422. */
export function commitUpload(baseUrl, token, fileId) {
  return apiPost(baseUrl, `/api/files/${fileId}/commit`, null, 'file_commit', token);
}

/** Trades the bearer token for the single-use WS handshake ticket (endpoint is not rate-limited). */
export function realtimeTicket(baseUrl, token) {
  return apiPost(baseUrl, '/api/realtime/ticket', null, 'ticket', token);
}
