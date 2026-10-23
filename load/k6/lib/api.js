// Thin HTTP helpers for target A (forum-dotnet JSON API). Everything is tagged with { endpoint } so the
// summary (and the thesis tables) break latency out per logical operation, and every helper returns
// the raw k6 response — the adapter owns checks and metrics.
//
// Read helpers take an optional `opts` = { endpoint?: string, discard?: boolean, token?: string }: `endpoint`
// re-tags a setup-time call (so pool discovery never pollutes a journey's endpoint row), `discard` drops a body the
// journey never parses (lib/wire.js DISCARD) and `token` makes an otherwise anonymous read a signed-in one (the
// AUTH_READS variant, deviation D3). Every request carries the browser's Accept-Encoding.

import http from 'k6/http';

import { BROWSER_HEADERS, DISCARD } from './wire.js';

/** Every request carries the endpoint tag; JSON bodies get the SPA's Content-Type. */
function params(endpoint, token, extra = {}) {
  const headers = { Accept: 'application/json', ...BROWSER_HEADERS, ...(extra.headers || {}) };
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }
  return { ...extra, headers, tags: { endpoint } };
}

export function apiGet(baseUrl, path, endpoint, token = null, opts = {}) {
  return http.get(`${baseUrl}${path}`, params(opts.endpoint || endpoint, token || opts.token || null, opts.discard ? DISCARD : {}));
}

export function apiPost(baseUrl, path, body, endpoint, token = null, opts = {}) {
  return http.post(`${baseUrl}${path}`, body === null ? null : JSON.stringify(body),
    params(opts.endpoint || endpoint, token, {
      ...(opts.discard ? DISCARD : {}),
      headers: body === null ? {} : { 'Content-Type': 'application/json' },
    }));
}

export function apiPut(baseUrl, path, body, endpoint, token = null, opts = {}) {
  return http.put(`${baseUrl}${path}`, body === null ? null : JSON.stringify(body),
    params(endpoint, token, {
      ...(opts.discard ? DISCARD : {}),
      headers: body === null ? {} : { 'Content-Type': 'application/json' },
    }));
}

export function apiDelete(baseUrl, path, endpoint, token = null, opts = {}) {
  return http.del(`${baseUrl}${path}`, null, params(endpoint, token, opts.discard ? DISCARD : {}));
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
export function listCategories(baseUrl, opts = {}) {
  return apiGet(baseUrl, '/api/content/categories', 'categories', null, opts);
}

/** Category feed page (keyset). cursor null → first page. */
export function threadFeed(baseUrl, categoryId, cursor = null, limit = 20, opts = {}) {
  const cursorPart = cursor ? `&cursor=${encodeURIComponent(cursor)}` : '';
  return apiGet(baseUrl, `/api/content/threads?categoryId=${categoryId}&limit=${limit}${cursorPart}`, 'feed', null, opts);
}

/** The SPA's core thread-open calls: detail + comment tree + reaction batch (G22 parity; D1: idealized client). */
export function threadDetail(baseUrl, threadId, opts = {}) {
  return apiGet(baseUrl, `/api/content/threads/${threadId}`, 'thread', null, opts);
}

export function commentTree(baseUrl, threadId, opts = {}) {
  return apiGet(baseUrl, `/api/content/threads/${threadId}/comments`, 'comments', null, opts);
}

export function reactionBatch(baseUrl, targetType, targetIds, opts = {}) {
  const ids = encodeURIComponent(targetIds.join(','));
  return apiGet(baseUrl, `/api/engagement/reactions/batch?targetType=${targetType}&targetIds=${ids}`, 'reactions_batch', null, opts);
}

export function searchThreads(baseUrl, query, opts = {}) {
  return apiGet(baseUrl, `/api/content/search?q=${encodeURIComponent(query)}&limit=20`, 'search', null, opts);
}

export function suggestTags(baseUrl, prefix) {
  return apiGet(baseUrl, `/api/content/tags?query=${encodeURIComponent(prefix)}&limit=20`, 'tags');
}

export function createComment(baseUrl, token, threadId, body, opts = {}) {
  return apiPost(baseUrl, `/api/content/threads/${threadId}/comments`, { parentId: null, body }, 'comment_create', token, opts);
}

export function createThread(baseUrl, token, categoryId, title, body, tagSlugs) {
  return apiPost(baseUrl, '/api/content/threads', { categoryId, title, body, tagSlugs }, 'thread_create', token);
}

/**
 * Idempotent both directions (a re-like / un-like-never-liked is a 200 NO-OP with no write) — so the adapter
 * keeps per-VU like state and always sends the state-changing direction, like the SPA's button (and like B's
 * server-side vote toggle). See targets/a.js J5.
 */
export function addReaction(baseUrl, token, threadId, opts = {}) {
  return apiPut(baseUrl, `/api/engagement/reactions/thread/${threadId}`, null, 'reaction_add', token, opts);
}

export function removeReaction(baseUrl, token, threadId, opts = {}) {
  return apiDelete(baseUrl, `/api/engagement/reactions/thread/${threadId}`, 'reaction_remove', token, opts);
}

export function userStats(baseUrl, userId, opts = {}) {
  return apiGet(baseUrl, `/api/engagement/users/${userId}/stats`, 'user_stats', null, opts);
}

export function userThreads(baseUrl, userId, opts = {}) {
  return apiGet(baseUrl, `/api/content/users/${userId}/threads`, 'user_threads', null, opts);
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

/** Files: bind a committed file to a target (the SPA composer attaches READY files to the created thread). */
export function attachFile(baseUrl, token, fileId, targetType, targetId) {
  return apiPost(baseUrl, `/api/files/${fileId}/attachments`, { targetType, targetId }, 'file_attach', token);
}

/** Social: get-or-create the direct conversation with a user (200 + conversationId either way). */
export function openDirectConversation(baseUrl, token, userId) {
  return apiPost(baseUrl, '/api/social/conversations/direct', { userId }, 'dm_open', token);
}

/** Social: send a message into a conversation (200 + messageId; realtime push to both user views). */
export function sendMessage(baseUrl, token, conversationId, body, opts = {}) {
  return apiPost(baseUrl, `/api/social/conversations/${conversationId}/messages`, { body }, 'dm_send', token, opts);
}
