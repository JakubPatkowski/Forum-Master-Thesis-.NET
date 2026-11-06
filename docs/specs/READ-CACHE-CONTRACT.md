# Read-cache contract for the v5 A/B rounds

> **Audience:** both authors and every AI agent working on Architecture A (`forum-dotnet`) or Architecture B (`gomx`).
> **Status:** binding for the v5 measurement rounds (tags `thesis-v5-a` / `thesis-v5-b`, harness `h7`). A change to
> this file needs both authors' agreement; an implementation that cannot follow a row reports it instead of
> silently deviating.

## 1. Why this exists

B's branch `feature/improvements-libraries` (2026-10-30…11-01) added in-process data microcaches with
singleflight. That broke fairness invariant **F6 "no cache tier on one side only"**
(`thesis/results/AB-COMPARISON-v1.md` §6.1). v5 resolves it by giving A the **same** cache and making the cache
switchable on **both** sides. Every round is either `read-cache off` on both sides or `read-cache on` on both
sides — never mixed.

## 2. What is cached — exactly these reads, nothing else on a measured path

| # | Read | A endpoint | B route | Cache key | TTL |
|---|---|---|---|---|---|
| C1 | one space's feed page (first page and every cursor page) | `GET /api/content/threads?categoryId=&cursor=&limit=` | `GET /c/{slug}` and its feed sentinel | space id, sort, cursor, limit (viewer-independent) | 2 s |
| C2 | one thread's comment tree | `GET /api/content/threads/{id}/comments` | comment tree of `GET /thread/{id}` (and B's paged variant) | thread id | 2 s |
| C3 | one search result page | `GET /api/content/search?q=&sort=&cursor=&limit=` | `GET /search?q=&sort=` | viewer scope, normalized query, sort, filter, cursor/offset, limit | 3 s |

- **Viewer scope** in C3 = `anonymous` or the signed-in user id (private spaces are visible to some users only).
- C1 and C2 are stored **only after** the request passed the visibility / read gate; a cache hit never skips that
  gate. Response-level caching in front of the gate (e.g. ASP.NET Core `OutputCache` middleware) is not allowed.
- Not cached on either side: thread detail, reaction / vote counts and batches, profiles, the space list, DMs,
  notifications, sessions, anything signed-in that is not in the table.
- B's home-feed cache (`vis:` keys) is not on a measured path and may stay.
- Allowed and **not** a data cache: a content-addressed render memo whose key is the source text itself
  (B's markdown cache). It can never return stale data.

## 3. Invalidation — local to the replica that handled the write, right after the write is durable

| Write | Flush |
|---|---|
| thread create, update, delete, pin, move to another space | C1 all, C2 all, C3 all |
| comment create, update | C2 of that thread |
| comment delete | C1 all, C2 all |
| reaction / vote toggle on a thread | nothing (cards may show counts up to one TTL old) |
| like on a comment | C2 of that thread on B; nothing on A (A's tree carries no counts) |
| space create, update, delete | C1 all, C2 all, C3 all |

- **No cross-replica invalidation** on either side; the other replica converges within the TTL.
- **Generation guard:** a result computed across an invalidation is returned to its waiters but not stored
  (B: generation counter in `internal/cache/cache.go`).

## 4. Mechanics both sides share

- In-process, per replica; bounded at about 1 000 entries per cache.
- Concurrent misses on one key share one computation (B: `singleflight`; A: an equivalent such as `HybridCache`'s
  built-in stampede protection).
- A waiter whose request is cancelled stops waiting; the shared computation is not cancelled for the others.
- A hit returns the stored result without re-running the query or rebuilding the tree.
- Metrics: lookups per cache and result (`hit`, `miss`, `bypass`) — A `forum.read_cache.lookups`,
  B `gomx_read_cache_lookups_total`.

## 5. Switch

| | A | B |
|---|---|---|
| Variable | `ReadCache__Enabled` (`true` / `false`) | `GOMX_READ_CACHE` (`1` / `0`) |
| Default in the app | `false` (`appsettings.json`) | `1` (product) |
| Default in the bench deployment | `false` (backend ConfigMap) | `0` (bench overlay) |
| Per round | `scripts/ab-round.sh --read-cache on|off` (default `off`) sets both, `ab_check_profile` verifies both, `meta-*.json` and `round.json` record it | same |

Disabled means every read goes to the store: no lookup, no singleflight, no stored entry.

## 6. Measured versions and freeze

- A: `thesis-v5-a` (branch `33-feat-phase-14---ab-v5-alignment` or the branch the owner names), B: `thesis-v5-b`
  (`feature/master-thesis` after the fast-forward of `feature/improvements-libraries`), harness `h7`.
- After both tags exist, neither side changes code on the measured surface until the v5 rounds are closed.
  `feature/admin-panel` is not part of v5.

## 7. Sign-off

| Author | Agreed (date) |
|---|---|
| Jakub Patkowski (A) | — |
| Hubert Ożarowski (B) | — |
