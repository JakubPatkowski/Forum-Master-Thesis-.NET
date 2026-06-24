# Permissions & ACL — SQL-resolved RBAC + bitmask ACL

> simplified to the forum domain. The defining idea: **effective permissions are resolved in PostgreSQL
> (functions + aggregate + cache), not in C#.** The application asks one question — *"what can this user do
> here?"* — and the database answers in a single round-trip.

## Why this design

A naive RBAC computes `effective = (∪ role permissions) ∪ grants \ denies` in application code, issuing
several queries per check. A more efficient pattern instead:

1. Encodes permissions as **integer bitmasks** (`allow_bits`, `deny_bits`) — set operations become bitwise `|` / `& ~`.
2. Aggregates masks across sources with a **custom Postgres aggregate** (`int_or_agg`).
3. Resolves the final mask with **SQL functions** that union role grants, direct ACL entries and object policies.
4. Caches the result in an **`effective_perm_cache`** table with **partial + BRIN indexes** for hot-path reads.

Result: O(1) permission checks, set-algebra in the engine, auditable in SQL.

## Core concepts (forum-scoped)

| Concept | Forum meaning |
|---|---|
| **Action** | An atomic capability, one bit: `read, create, update, delete, comment, like, moderate` |
| **Permission mask** | Integer where each bit = one action (`PermissionMask` value object) |
| **Scope** | Where a grant applies: `global` · `category` · `thread` |
| **Principal** | Who holds it: `user` · `role` (group optional later) |
| **Role** | Named bundle (`user < moderator < admin`) → permission template |
| **ACL entry** | `(scope, scope_id, principal) → allow_bits / deny_bits` (per-object override) |
| **Object policy** | Per object-type rules (e.g. private category requires membership) |
| **Effective** | `aggregate(allow) & ~aggregate(deny)`, materialized in `effective_perm_cache` |

## Schema (DDL sketch)

```sql
CREATE SCHEMA IF NOT EXISTS forum_authz;

-- ULID-as-text domain (sortable, validated)
CREATE DOMAIN forum_authz.ulid26 AS text
  CHECK (VALUE ~ '^[0-9A-HJKMNP-TV-Z]{26}$');

-- Catalog of actions → bit position (single source of truth for the mask layout)
CREATE TABLE forum_authz.actions (
  code        text PRIMARY KEY,           -- 'thread.create'
  bit         int  NOT NULL UNIQUE        -- 0..30
);

CREATE TABLE forum_authz.roles (
  role_id     forum_authz.ulid26 PRIMARY KEY,
  name        text NOT NULL,
  allow_bits  int  NOT NULL DEFAULT 0     -- the role's permission template
);

CREATE TABLE forum_authz.user_roles (
  user_id     forum_authz.ulid26 NOT NULL,
  role_id     forum_authz.ulid26 NOT NULL REFERENCES forum_authz.roles(role_id),
  PRIMARY KEY (user_id, role_id)
);

-- Direct ACL: per-principal allow/deny masks at a given scope (the "list" you wrote in SQL)
CREATE TABLE forum_authz.acl_entries (
  acl_id          forum_authz.ulid26 PRIMARY KEY,
  scope           text NOT NULL,                 -- 'global' | 'category' | 'thread'
  scope_id        forum_authz.ulid26,            -- NULL for global
  principal_type  text NOT NULL,                 -- 'user' | 'role'
  principal_id    forum_authz.ulid26 NOT NULL,
  allow_bits      int  NOT NULL DEFAULT 0,
  deny_bits       int  NOT NULL DEFAULT 0,
  created_on_utc  timestamptz NOT NULL DEFAULT now()
);

-- Precomputed answer per (user, scope, scope_id)
CREATE TABLE forum_authz.effective_perm_cache (
  user_id     forum_authz.ulid26 NOT NULL,
  scope       text NOT NULL,
  scope_id    forum_authz.ulid26,
  allow_bits  int  NOT NULL,
  deny_bits   int  NOT NULL,
  effective   int  NOT NULL,                      -- allow & ~deny
  computed_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (user_id, scope, scope_id)
);
```

## Bitwise aggregate (the clever bit)

```sql
CREATE OR REPLACE FUNCTION forum_authz.int_or(a int, b int)
  RETURNS int LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$ SELECT COALESCE(a,0) | COALESCE(b,0) $$;

CREATE AGGREGATE forum_authz.int_or_agg (int) (
  SFUNC = forum_authz.int_or, STYPE = int, INITCOND = '0'
);
```

## Resolver function

```sql
-- Returns the effective mask for a user at a scope, unioning role templates,
-- role-level ACL and user-level ACL, then subtracting denies.
CREATE OR REPLACE FUNCTION forum_authz.effective_mask(
    p_user  forum_authz.ulid26,
    p_scope text,
    p_scope_id forum_authz.ulid26 DEFAULT NULL)
  RETURNS int LANGUAGE sql STABLE AS $$
  WITH role_ids AS (
      SELECT role_id FROM forum_authz.user_roles WHERE user_id = p_user
  ),
  allow AS (
      SELECT forum_authz.int_or_agg(bits) AS m FROM (
          SELECT allow_bits AS bits FROM forum_authz.roles      WHERE role_id IN (SELECT role_id FROM role_ids)
          UNION ALL
          SELECT allow_bits FROM forum_authz.acl_entries
            WHERE (scope, COALESCE(scope_id,'')) = (p_scope, COALESCE(p_scope_id,''))
              AND ((principal_type='user' AND principal_id=p_user)
                OR (principal_type='role' AND principal_id IN (SELECT role_id FROM role_ids)))
      ) s
  ),
  deny AS (
      SELECT forum_authz.int_or_agg(deny_bits) AS m FROM forum_authz.acl_entries
        WHERE (scope, COALESCE(scope_id,'')) = (p_scope, COALESCE(p_scope_id,''))
          AND ((principal_type='user' AND principal_id=p_user)
            OR (principal_type='role' AND principal_id IN (SELECT role_id FROM role_ids)))
  )
  SELECT COALESCE((SELECT m FROM allow),0) & ~COALESCE((SELECT m FROM deny),0);
$$;
```

A single `SELECT (forum_authz.effective_mask(:user,'thread',:id) & :bit) <> 0` answers any check.

## Indexing (hot path)

```sql
-- Composite "hotpath" index for the resolver's ACL lookups
CREATE INDEX ix_acl_hotpath ON forum_authz.acl_entries (scope, scope_id, principal_type, principal_id);
-- Partial indexes: only rows that actually grant/deny something
CREATE INDEX ix_acl_allow ON forum_authz.acl_entries (principal_id) WHERE allow_bits <> 0;
CREATE INDEX ix_acl_deny  ON forum_authz.acl_entries (principal_id) WHERE deny_bits  <> 0;
-- BRIN for the append-only cache (cheap, time-correlated)
CREATE INDEX ix_effperm_brin ON forum_authz.effective_perm_cache USING brin (computed_at) WITH (pages_per_range = 128);
```

## How it maps to the .NET app

- `PermissionMask` value object (in `Forum.Core.Domain/Modules/Identity`) wraps the `int`; bit layout mirrors `actions`.
- The functions/aggregate/domain + indexes ship as **EF Core migrations with raw SQL** in
  `Forum.Adapters.Out.Persistence/Acl/` and are (re)applied by the migration **Job** (not at app startup).
- `ICurrentUser.Has(code)` calls the resolver (or reads `effective_perm_cache`); REST endpoints gate with
  `RequirePermission("thread.create")`. Validation order stays **404 → 403 → 422**.
- Cache invalidation: a domain event (`RoleAssigned`, `AclEntryChanged`) enqueues a recompute for affected users.

## Forum-scoped simplifications

Dropped (SaaS-only): workspaces, teams, plugins, multi-tenant scopes, role templates table, verbose maps.
Kept (the professional core): bitmask masks, `int_or_agg`, SQL resolver, effective cache, partial + BRIN indexes,
ULID domain, allow/deny separation. This is "professionally over-engineered" without SaaS baggage the thesis doesn't need.
