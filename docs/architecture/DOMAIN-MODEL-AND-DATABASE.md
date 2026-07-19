# Domain model & database — Architecture A

> **Status:** authoritative design (DDL is an illustrative sketch; the real schema ships as per-module EF Core
> migrations). **Updated:** 2026-06-24. Companion to
> [`REQUIREMENTS-AND-ASSUMPTIONS.md`](./REQUIREMENTS-AND-ASSUMPTIONS.md) and
> [`permissions-acl-design.md`](../db/permissions-acl-design.md).
>
> Modeled on `ProjektForumWedkarskie` (roles/permissions/refresh-tokens, materialized-path comments, FTS,
> keyset index) and extended to the thesis requirements (ULID, bitmask ACL, ownership, soft-delete, full audit).

---

## 1. Conventions (apply to every table)

- **Database:** PostgreSQL 17, single database `forum_net`, **one schema per module**
  (`forum_identity`, `forum_authz`, `forum_content`, `forum_files`, `forum_engagement`, `forum_social`,
  `forum_audit`). No FK crosses a schema boundary (modular-monolith rule — cross-module links are logical
  ULIDs kept consistent by integration events).
- **Primary keys:** **ULID** for every aggregate. Stored as a validated text domain so it stays sortable and
  index-friendly:
  ```sql
  CREATE DOMAIN forum.ulid26 AS text CHECK (VALUE ~ '^[0-9A-HJKMNP-TV-Z]{26}$');
  ```
  (Alternatively `uuid` with ULID-ordered bytes; the project standardizes on the text domain to keep the value
  human-readable in logs and URLs. One choice, applied everywhere.)
- **Audit (every aggregate root):** `created_on_utc timestamptz NOT NULL DEFAULT now()`, `created_by ulid26`,
  `last_modified_on_utc timestamptz`, `last_modified_by ulid26`. Stamped by the EF **AuditInterceptor**.
- **Soft-delete (removable aggregates):** `is_deleted boolean NOT NULL DEFAULT false`, `deleted_on_utc timestamptz`,
  `deleted_by ulid26`. Global EF query filter hides deleted rows.
- **Ownership (owned aggregates):** `owner_id ulid26 NOT NULL` (creator). `created_by` may equal `owner_id`;
  `owner_id` is the authorization subject, `created_by` is the audit fact.
- **Timestamps** are UTC `timestamptz`. **Naming** snake_case in DB. **Enums** as Postgres `ENUM` types.
- **Reads** use SQL **views** + **keyset** pagination; **FTS** via `tsvector` + GIN + trigger.

---

## 2. Identity module (`forum_identity`)

```sql
CREATE TYPE forum_identity.user_status AS ENUM ('active','blocked','pending_verification');

CREATE TABLE forum_identity.users (
  id                   forum.ulid26 PRIMARY KEY,
  username             text NOT NULL,
  username_lc          text NOT NULL UNIQUE,                 -- case-insensitive uniqueness
  email                citext NOT NULL UNIQUE,               -- required (login/reset)
  display_name         text NOT NULL,
  password_hash        text NOT NULL,                        -- Argon2id encoded string (ADR 0007)
  status               forum_identity.user_status NOT NULL DEFAULT 'active',
  avatar_file_id       forum.ulid26,                         -- logical ref to forum_files.files
  -- audit
  created_on_utc       timestamptz NOT NULL DEFAULT now(),
  created_by           forum.ulid26,
  last_modified_on_utc timestamptz,
  last_modified_by     forum.ulid26
);
CREATE INDEX ix_users_status ON forum_identity.users (status);

-- Refresh-token rotation + reuse-detection (token family). Only the HASH is stored.
CREATE TYPE forum_identity.token_status AS ENUM ('active','rotated','revoked');
CREATE TABLE forum_identity.refresh_tokens (
  id             forum.ulid26 PRIMARY KEY,
  user_id        forum.ulid26 NOT NULL REFERENCES forum_identity.users(id) ON DELETE CASCADE,
  family_id      forum.ulid26 NOT NULL,                      -- all tokens rotated from one login
  token_hash     text NOT NULL,                              -- SHA-256 of the opaque token
  status         forum_identity.token_status NOT NULL DEFAULT 'active',
  expires_on_utc timestamptz NOT NULL,
  created_on_utc timestamptz NOT NULL DEFAULT now(),
  rotated_to     forum.ulid26,                               -- next token in the chain
  ip             inet,
  user_agent     text
);
CREATE INDEX ix_refresh_user ON forum_identity.refresh_tokens (user_id);
CREATE UNIQUE INDEX ux_refresh_hash ON forum_identity.refresh_tokens (token_hash);
```

Rules: reuse of an already-`rotated`/`revoked` token revokes the whole `family_id` (theft detection). Access
token (JWT, 15 min) is not stored; refresh (14 d) lives only as a hash, in an httpOnly cookie on the client.

### Authz tables (`forum_authz`) — see `permissions-acl-design.md`

Roles/permissions/ACL are the RBAC + bitmask design. Global roles `user < moderator < admin`; **per-context
roles** are expressed as ACL entries scoped to `category` (or `group`) — e.g. a user who is a category
moderator has an `acl_entries` row at `scope='category', scope_id=<cat>` granting `moderate`. Tables:
`actions`, `roles`, `user_roles`, `acl_entries (scope, scope_id, principal, allow_bits, deny_bits)`,
`effective_perm_cache`, plus the `int_or_agg` aggregate and `effective_mask()` resolver. **Per-context groups**
(optional) add a `groups` + `group_members` pair whose membership feeds the resolver as another principal.

---

## 3. Content module (`forum_content`)

```sql
CREATE TYPE forum_content.visibility AS ENUM ('public','private');

CREATE TABLE forum_content.categories (
  id                   forum.ulid26 PRIMARY KEY,
  slug                 text NOT NULL UNIQUE,
  name                 text NOT NULL,
  description          text NOT NULL DEFAULT '',
  visibility           forum_content.visibility NOT NULL DEFAULT 'public',
  owner_id             forum.ulid26 NOT NULL,                -- creator/owner (user ULID)
  icon_file_id         forum.ulid26,                         -- logical ref to forum_files
  created_on_utc       timestamptz NOT NULL DEFAULT now(),
  created_by           forum.ulid26,
  last_modified_on_utc timestamptz,
  last_modified_by     forum.ulid26,
  is_deleted           boolean NOT NULL DEFAULT false,
  deleted_on_utc       timestamptz,
  deleted_by           forum.ulid26
);

CREATE TABLE forum_content.threads (
  id                   forum.ulid26 PRIMARY KEY,
  category_id          forum.ulid26 NOT NULL REFERENCES forum_content.categories(id),
  owner_id             forum.ulid26 NOT NULL,                -- author
  title                text NOT NULL,
  body                 text NOT NULL,                        -- markdown
  search_tsv           tsvector,
  is_pinned            boolean NOT NULL DEFAULT false,
  created_on_utc       timestamptz NOT NULL DEFAULT now(),
  created_by           forum.ulid26,
  last_modified_on_utc timestamptz,
  last_modified_by     forum.ulid26,
  is_deleted           boolean NOT NULL DEFAULT false,
  deleted_on_utc       timestamptz,
  deleted_by           forum.ulid26
);
-- keyset feed index (pinned first, then newest); ULID id breaks ties deterministically
CREATE INDEX ix_threads_feed ON forum_content.threads (category_id, is_pinned DESC, created_on_utc DESC, id DESC)
  WHERE is_deleted = false;
CREATE INDEX ix_threads_search ON forum_content.threads USING gin (search_tsv);

-- Nested comments via materialized path (max depth 5), soft-delete keeps children.
CREATE TABLE forum_content.comments (
  id                   forum.ulid26 PRIMARY KEY,
  thread_id            forum.ulid26 NOT NULL REFERENCES forum_content.threads(id) ON DELETE CASCADE,
  parent_id            forum.ulid26 REFERENCES forum_content.comments(id) ON DELETE CASCADE,
  owner_id             forum.ulid26 NOT NULL,                -- author
  body                 text NOT NULL,                        -- "[deleted]" when soft-deleted
  path                 text NOT NULL,                        -- materialized path, e.g. '<ulid>.<ulid>'
  depth                int  NOT NULL DEFAULT 0,               -- <= 5
  created_on_utc       timestamptz NOT NULL DEFAULT now(),
  created_by           forum.ulid26,
  last_modified_on_utc timestamptz,
  last_modified_by     forum.ulid26,
  is_deleted           boolean NOT NULL DEFAULT false,
  deleted_on_utc       timestamptz,
  deleted_by           forum.ulid26
);
CREATE INDEX ix_comments_thread_path ON forum_content.comments (thread_id, path);

CREATE TABLE forum_content.tags (
  id    forum.ulid26 PRIMARY KEY,
  slug  text NOT NULL UNIQUE,
  name  text NOT NULL
);
CREATE TABLE forum_content.thread_tags (
  thread_id forum.ulid26 NOT NULL REFERENCES forum_content.threads(id) ON DELETE CASCADE,
  tag_id    forum.ulid26 NOT NULL REFERENCES forum_content.tags(id)    ON DELETE CASCADE,
  PRIMARY KEY (thread_id, tag_id)
);
```

FTS trigger fills `search_tsv` from `title` (weight A) + `body` (weight B), as in forum-wędkarskie. Comment tree
read with `ORDER BY path` (DFS); a comment's `path` = parent path + own ULID.

---

## 4. Files module (`forum_files`) — direct-to-MinIO (ADR 0008)

```sql
CREATE TYPE forum_files.file_status AS ENUM ('pending','committed');

CREATE TABLE forum_files.files (
  id              forum.ulid26 PRIMARY KEY,
  bucket          text NOT NULL,
  object_key      text NOT NULL,                 -- MinIO key (content-addressed prefix)
  content_type    text NOT NULL,
  size_bytes      bigint NOT NULL DEFAULT 0,
  width           int,
  height          int,
  status          forum_files.file_status NOT NULL DEFAULT 'pending',
  uploaded_by     forum.ulid26 NOT NULL,         -- the user who requested the presigned URL
  created_on_utc  timestamptz NOT NULL DEFAULT now(),
  committed_on_utc timestamptz,
  UNIQUE (bucket, object_key)
);

-- THE "file belongs to which object" link (requirement #1): a logical FK to the target aggregate.
CREATE TYPE forum_files.attach_target AS ENUM ('thread','comment','category_icon','avatar','dm');
CREATE TABLE forum_files.file_attachments (
  file_id      forum.ulid26 NOT NULL REFERENCES forum_files.files(id) ON DELETE CASCADE,
  target_type  forum_files.attach_target NOT NULL,
  target_id    forum.ulid26 NOT NULL,            -- ULID of the thread/comment/category/user/dm
  created_on_utc timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (file_id, target_type, target_id)
);
CREATE INDEX ix_attach_target ON forum_files.file_attachments (target_type, target_id);
```

Upload flow: client requests a presigned PUT (backend creates a `pending` row + returns URL + key) → client PUTs
bytes **straight to MinIO** → client calls *commit* → backend HEADs the object, validates type/size, flips to
`committed`, records dimensions. A periodic sweep deletes `pending` rows older than a grace window and blobs with
no `committed` attachment. `target_type/target_id` is the foreign-object reference (kept consistent via
`ThreadDeleted`/`CommentDeleted` consumers — no cross-schema DB FK).

---

## 5. Engagement module (`forum_engagement`)

```sql
CREATE TYPE forum_engagement.reaction_target AS ENUM ('thread','comment');
CREATE TABLE forum_engagement.reactions (
  user_id      forum.ulid26 NOT NULL,           -- logical ref to identity
  target_type  forum_engagement.reaction_target NOT NULL,
  target_id    forum.ulid26 NOT NULL,
  value        smallint NOT NULL DEFAULT 1,      -- +1 like/upvote (extensible to -1)
  created_on_utc timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (user_id, target_type, target_id)
);
CREATE INDEX ix_reactions_target ON forum_engagement.reactions (target_type, target_id);
```

Counters are **read-time aggregates from a view** (`user_stats`, `thread_counts`) — but, unlike B, A denormalizes
hot counters where the benchmark needs them (or uses a materialized view refreshed on `ReactionAdded/Removed`) to
avoid full-table scans on the feed. (Decision recorded with the perf work.)

---

## 6. Social module (`forum_social`) — SHIPPED (Phase 11, 2026-07-17)

> The original two-table sketch (friendships + direct_messages) was superseded by the real build: groups became
> mandatory for B-parity, DMs and group chat share ONE unified messaging pipeline, and blocks/notifications/
> privacy/presence joined the scope. All enums are stored as text (Content precedent), never PG enums. Group
> roles are NOT a column — group-admin is the ACL's `moderate` bit at `scope='group'` (`permissions-acl-design.md`);
> the membership row itself is the only "is in the group" fact. Migrations: `InitialSocial` (EF) +
> `AddSocialViews` (raw SQL views).

```sql
-- One row per pair; decline/cancel/unfriend DELETE it (no declined tombstones).
CREATE TABLE forum_social.friendships (
  id             varchar(26) PRIMARY KEY,
  requester_id   varchar(26) NOT NULL,
  addressee_id   varchar(26) NOT NULL,
  status         varchar(16) NOT NULL,            -- 'pending' | 'accepted'
  accepted_on_utc timestamptz,
  -- + full audit (created/last_modified by/on)
  CONSTRAINT ux_friendships_pair_directed UNIQUE (requester_id, addressee_id)
);
-- Raw-SQL in InitialSocial: closes the A→B / B→A race.
CREATE UNIQUE INDEX ux_friendships_pair ON forum_social.friendships
  (LEAST(requester_id, addressee_id), GREATEST(requester_id, addressee_id));

-- Peer block (NOT Identity's admin ban): suppresses requests/DMs/invites both directions.
CREATE TABLE forum_social.social_blocks (
  blocker_id varchar(26) NOT NULL,
  blocked_id varchar(26) NOT NULL,
  created_on_utc timestamptz NOT NULL,
  PRIMARY KEY (blocker_id, blocked_id)
);

CREATE TABLE forum_social.groups (
  id varchar(26) PRIMARY KEY,
  name varchar(100) NOT NULL,
  description varchar(2000) NOT NULL,
  visibility varchar(16) NOT NULL,                -- 'public' (join freely) | 'private' (invite-only)
  owner_id varchar(26) NOT NULL,                  -- IOwned; owner can never leave/be kicked
  -- + soft-delete + full audit
  is_deleted boolean NOT NULL DEFAULT false
);

CREATE TABLE forum_social.group_memberships (     -- THE membership fact (owner has a row too)
  group_id varchar(26) NOT NULL REFERENCES forum_social.groups(id) ON DELETE CASCADE,
  user_id varchar(26) NOT NULL,
  joined_on_utc timestamptz NOT NULL,
  invited_by varchar(26),
  PRIMARY KEY (group_id, user_id)
);

CREATE TABLE forum_social.group_invites (         -- pending only; accept/decline/cancel DELETE the row
  id varchar(26) PRIMARY KEY,
  group_id varchar(26) NOT NULL REFERENCES forum_social.groups(id) ON DELETE CASCADE,
  invited_user_id varchar(26) NOT NULL,
  invited_by varchar(26) NOT NULL,
  -- + full audit
  CONSTRAINT ux_group_invites_pending UNIQUE (group_id, invited_user_id)
);

-- ONE messaging pipeline for DMs and group chat. A group chat's conversation id == the group's id.
-- Direct conversations are get-or-created lazily; direct_key = 'loUlid:hiUlid' makes that race-safe.
CREATE TABLE forum_social.conversations (
  id varchar(26) PRIMARY KEY,
  type varchar(16) NOT NULL,                      -- 'direct' | 'group'
  direct_key varchar(53)                          -- NULL for group chats
  -- + audit
);
CREATE UNIQUE INDEX ux_conversations_direct_key
  ON forum_social.conversations (direct_key) WHERE direct_key IS NOT NULL;

-- THE single message-authorization fact (group membership writes through here in the same tx).
-- last_read_on_utc = the OWNER's unread badge only; read receipts to the sender are out of scope.
CREATE TABLE forum_social.conversation_participants (
  conversation_id varchar(26) NOT NULL REFERENCES forum_social.conversations(id) ON DELETE CASCADE,
  user_id varchar(26) NOT NULL,
  joined_on_utc timestamptz NOT NULL,
  left_on_utc timestamptz,                        -- set on leave/kick; row kept for attribution
  last_read_on_utc timestamptz,
  is_muted boolean NOT NULL DEFAULT false,
  PRIMARY KEY (conversation_id, user_id)
);

CREATE TABLE forum_social.messages (              -- delete = tombstone: body -> '[deleted]', row kept
  id varchar(26) PRIMARY KEY,
  conversation_id varchar(26) NOT NULL REFERENCES forum_social.conversations(id) ON DELETE CASCADE,
  owner_id varchar(26) NOT NULL,                  -- the sender (IOwned)
  body varchar(4000) NOT NULL,
  edited_on_utc timestamptz,
  -- + soft-delete + full audit
  is_deleted boolean NOT NULL DEFAULT false
);
CREATE INDEX ix_messages_history ON forum_social.messages (conversation_id, id DESC);  -- keyset

-- Durable bell truth (ADR 0010: the WS ping is identity+routing; clients re-fetch these rows).
-- Kinds: friend.request | friend.accepted | group.invite | group.invite.accepted | group.kicked.
-- Message arrivals do NOT create rows (badge derives from last_read_on_utc).
CREATE TABLE forum_social.notifications (
  id varchar(26) PRIMARY KEY,
  user_id varchar(26) NOT NULL,
  kind varchar(32) NOT NULL,
  actor_id varchar(26),
  target_id varchar(26),
  is_read boolean NOT NULL DEFAULT false,
  created_on_utc timestamptz NOT NULL
);
CREATE INDEX ix_notifications_feed ON forum_social.notifications (user_id, id DESC);
CREATE INDEX ix_notifications_unread ON forum_social.notifications (user_id) WHERE is_read = false;

CREATE TABLE forum_social.user_privacy_settings ( -- absent row = defaults (everyone/everyone/everyone/true)
  user_id varchar(26) PRIMARY KEY,
  friend_requests varchar(16) NOT NULL,           -- 'everyone' | 'no_one' ('friends' normalizes to no_one)
  messages varchar(16) NOT NULL,                  -- 'everyone' | 'friends' | 'no_one'
  group_invites varchar(16) NOT NULL,
  show_online_status boolean NOT NULL
);

CREATE TABLE forum_social.user_presence (         -- ephemeral; status computed from age at read time
  user_id varchar(26) PRIMARY KEY,                -- behind IPresenceStore (the Redis-swap seam)
  last_heartbeat_on_utc timestamptz NOT NULL
);
```

Read views (`AddSocialViews`, view-level read joins into `forum_identity.users` + `forum_authz.acl_entries`):
`friend_list_v` (accepted pairs expanded per direction), `friend_request_v`, `blocked_list_v`, `group_list_v`
(+member_count), `group_member_v` (is_admin resolved live from the ACL's `moderate` bit at group scope —
bit looked up from the actions catalog, never hardcoded), `group_invite_v`, `conversation_list_v` (display
name, last-message lateral, per-seat unread count), `message_history_v` (tombstones kept, body masked),
`notification_list_v`.

---

## 7. Messaging infrastructure (shared)

Each module that publishes integration events owns an **outbox** table in its own schema:

```sql
CREATE TABLE <schema>.outbox_messages (
  id             forum.ulid26 PRIMARY KEY,
  occurred_on_utc timestamptz NOT NULL DEFAULT now(),
  type           text NOT NULL,                  -- versioned event name
  payload        jsonb NOT NULL,
  correlation_id text,
  processed_on_utc timestamptz,                  -- NULL until relayed to RabbitMQ
  attempts       int NOT NULL DEFAULT 0
);
CREATE INDEX ix_outbox_unprocessed ON <schema>.outbox_messages (occurred_on_utc) WHERE processed_on_utc IS NULL;
```

Consumers keep an **inbox**/dedupe table (`processed_event_ids`) for idempotency. See
[ADR 0009](./adr/0009-rabbitmq-inter-module-events.md).

---

## 8. Read models (views)

Examples (each owned by its module, `NoTracking`, keyset-friendly):

- `forum_content.thread_feed_v` — thread + author display + category slug + like/comment counts.
- `forum_content.comment_tree_v` — comment + author, ordered by `(thread_id, path)`.
- `forum_engagement.user_stats_v` — per-user karma, post/comment counts.

---

## 9. Migration ownership

Each module owns its migration chain (`dotnet ef migrations add … -c XDbContext`). The ACL functions/aggregate/
domain ship as **raw-SQL EF migrations** in Identity's `Infrastructure/Acl/`. All migrations are applied by the
**k8s migration Job** (`Forum.Api migrate`), never at pod startup ([ADR 0005](./adr/0005-migrations-as-k8s-job.md)).
```
