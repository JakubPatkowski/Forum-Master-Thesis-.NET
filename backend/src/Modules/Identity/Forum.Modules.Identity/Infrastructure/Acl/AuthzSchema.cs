namespace Forum.Modules.Identity.Infrastructure.Acl;

/// <summary>
/// The raw SQL for the <c>forum_authz</c> RBAC + bitmask-ACL schema (ADR 0004, <c>permissions-acl-design.md</c>):
/// the action/role/ACL tables, the <c>int_or_agg</c> aggregate, the <c>effective_mask()</c> resolver and its
/// <c>has_permission()</c> wrapper, the <c>effective_perm_cache</c> + recompute function, the hot-path indexes and the
/// role/action seed. Shipped as a raw-SQL EF migration and applied by the migration Job (never code-first).
/// </summary>
internal static class AuthzSchema
{
    public const string Up =
        """
        CREATE SCHEMA IF NOT EXISTS forum_authz;

        -- ULID-as-text domain (sortable, validated). Crockford base32 excludes I, L, O, U.
        CREATE DOMAIN forum_authz.ulid26 AS text
          CHECK (VALUE ~ '^[0-9A-HJKMNP-TV-Z]{26}$');

        -- Catalog of actions -> bit position (single source of truth for the mask layout).
        CREATE TABLE forum_authz.actions (
          code text PRIMARY KEY,
          bit  int  NOT NULL UNIQUE
        );

        CREATE TABLE forum_authz.roles (
          role_id    forum_authz.ulid26 PRIMARY KEY,
          name       text NOT NULL UNIQUE,
          allow_bits int  NOT NULL DEFAULT 0
        );

        CREATE TABLE forum_authz.user_roles (
          user_id forum_authz.ulid26 NOT NULL,
          role_id forum_authz.ulid26 NOT NULL REFERENCES forum_authz.roles(role_id) ON DELETE CASCADE,
          PRIMARY KEY (user_id, role_id)
        );

        -- Direct ACL: per-principal allow/deny masks at a scope (global | category | thread).
        CREATE TABLE forum_authz.acl_entries (
          acl_id         forum_authz.ulid26 PRIMARY KEY,
          scope          text NOT NULL,
          scope_id       forum_authz.ulid26,
          principal_type text NOT NULL,
          principal_id   forum_authz.ulid26 NOT NULL,
          allow_bits     int  NOT NULL DEFAULT 0,
          deny_bits      int  NOT NULL DEFAULT 0,
          created_on_utc timestamptz NOT NULL DEFAULT now()
        );

        -- Precomputed answer per (user, scope, scope_id). scope_id is NULL for global, so a COALESCE-based
        -- unique index (not a PK) enforces one row per scope.
        CREATE TABLE forum_authz.effective_perm_cache (
          user_id     forum_authz.ulid26 NOT NULL,
          scope       text NOT NULL,
          scope_id    forum_authz.ulid26,
          allow_bits  int  NOT NULL,
          deny_bits   int  NOT NULL,
          effective   int  NOT NULL,
          computed_at timestamptz NOT NULL DEFAULT now()
        );

        -- Bitwise OR aggregate: union of permission masks across sources.
        CREATE OR REPLACE FUNCTION forum_authz.int_or(a int, b int)
          RETURNS int LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $fn$ SELECT COALESCE(a,0) | COALESCE(b,0) $fn$;

        CREATE AGGREGATE forum_authz.int_or_agg (int) (
          SFUNC = forum_authz.int_or, STYPE = int, INITCOND = '0'
        );

        -- Effective mask = (role templates ∪ role ACL ∪ user ACL allow) & ~(role/user ACL deny), at a scope.
        CREATE OR REPLACE FUNCTION forum_authz.effective_mask(
            p_user  forum_authz.ulid26,
            p_scope text,
            p_scope_id forum_authz.ulid26 DEFAULT NULL)
          RETURNS int LANGUAGE sql STABLE AS $fn$
          WITH role_ids AS (
              SELECT role_id FROM forum_authz.user_roles WHERE user_id = p_user
          ),
          allow AS (
              SELECT forum_authz.int_or_agg(bits) AS m FROM (
                  SELECT allow_bits AS bits FROM forum_authz.roles WHERE role_id IN (SELECT role_id FROM role_ids)
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
        $fn$;

        -- Convenience: does the user hold one action (by code) at a scope?
        CREATE OR REPLACE FUNCTION forum_authz.has_permission(
            p_user forum_authz.ulid26, p_action text, p_scope text, p_scope_id forum_authz.ulid26 DEFAULT NULL)
          RETURNS boolean LANGUAGE sql STABLE AS $fn$
          SELECT (forum_authz.effective_mask(p_user, p_scope, p_scope_id)
                  & COALESCE((SELECT (1 << bit) FROM forum_authz.actions WHERE code = p_action), 0)) <> 0;
        $fn$;

        -- Recompute the user's cache rows (global + every scope they hold an ACL entry at).
        CREATE OR REPLACE FUNCTION forum_authz.recompute_user_perms(p_user forum_authz.ulid26)
          RETURNS void LANGUAGE plpgsql AS $fn$
        BEGIN
          DELETE FROM forum_authz.effective_perm_cache WHERE user_id = p_user;

          INSERT INTO forum_authz.effective_perm_cache (user_id, scope, scope_id, allow_bits, deny_bits, effective, computed_at)
          SELECT p_user, scope, scope_id, eff, 0, eff, now()
          FROM (
              SELECT scope, scope_id, forum_authz.effective_mask(p_user, scope, scope_id) AS eff
              FROM (
                  SELECT 'global'::text AS scope, NULL::forum_authz.ulid26 AS scope_id
                  UNION
                  SELECT DISTINCT scope, scope_id
                  FROM forum_authz.acl_entries
                  WHERE principal_type = 'user' AND principal_id = p_user
              ) scopes
          ) computed;
        END;
        $fn$;

        -- Hot-path + partial + BRIN indexes.
        CREATE INDEX ix_acl_hotpath ON forum_authz.acl_entries (scope, scope_id, principal_type, principal_id);
        CREATE INDEX ix_acl_allow   ON forum_authz.acl_entries (principal_id) WHERE allow_bits <> 0;
        CREATE INDEX ix_acl_deny    ON forum_authz.acl_entries (principal_id) WHERE deny_bits  <> 0;
        CREATE UNIQUE INDEX ux_effperm_user_scope ON forum_authz.effective_perm_cache (user_id, scope, COALESCE(scope_id, ''));
        CREATE INDEX ix_effperm_brin ON forum_authz.effective_perm_cache USING brin (computed_at) WITH (pages_per_range = 128);

        -- Seed: the action -> bit catalog and the global roles (user < moderator < admin).
        INSERT INTO forum_authz.actions (code, bit) VALUES
          ('read',0),('create',1),('update',2),('delete',3),('comment',4),('like',5),('moderate',6),('manage',7);

        INSERT INTO forum_authz.roles (role_id, name, allow_bits) VALUES
          ('00000000000000000000000001','user',63),
          ('00000000000000000000000002','moderator',127),
          ('00000000000000000000000003','admin',255);
        """;

    public const string Down = "DROP SCHEMA IF EXISTS forum_authz CASCADE;";

    /// <summary>
    /// Migration <c>OptimizeEffectiveMask</c> (2026-10-24): same algebra, one sargable pass over <c>acl_entries</c>,
    /// a cache fast path in <c>has_permission()</c>, and row triggers that drop a user's cache rows on ANY write to
    /// <c>user_roles</c> / <c>acl_entries</c> (so the fast path can never serve a mask older than the last write).
    /// </summary>
    public const string OptimizedResolversUp =
        """
        CREATE OR REPLACE FUNCTION forum_authz.effective_mask(
            p_user  forum_authz.ulid26,
            p_scope text,
            p_scope_id forum_authz.ulid26 DEFAULT NULL)
          RETURNS int LANGUAGE sql STABLE PARALLEL SAFE AS $fn$
          WITH role_ids AS (
              SELECT role_id FROM forum_authz.user_roles WHERE user_id = p_user
          ),
          templates AS (
              SELECT forum_authz.int_or_agg(r.allow_bits) AS m
              FROM forum_authz.roles r
              WHERE r.role_id IN (SELECT role_id FROM role_ids)
          ),
          scoped AS (
              -- ONE pass over the scope's entries (was two); the predicate is index-friendly on
              -- ix_acl_hotpath (scope, scope_id, principal_type, principal_id) — no COALESCE on scope_id.
              SELECT forum_authz.int_or_agg(e.allow_bits) AS allow_m,
                     forum_authz.int_or_agg(e.deny_bits)  AS deny_m
              FROM forum_authz.acl_entries e
              WHERE e.scope = p_scope
                AND ((p_scope_id IS NULL AND e.scope_id IS NULL) OR e.scope_id = p_scope_id)
                AND ((e.principal_type = 'user' AND e.principal_id = p_user)
                  OR (e.principal_type = 'role' AND e.principal_id IN (SELECT role_id FROM role_ids)))
          )
          SELECT (COALESCE((SELECT m FROM templates), 0) | COALESCE((SELECT allow_m FROM scoped), 0))
                 & ~COALESCE((SELECT deny_m FROM scoped), 0);
        $fn$;

        CREATE OR REPLACE FUNCTION forum_authz.has_permission(
            p_user forum_authz.ulid26, p_action text, p_scope text, p_scope_id forum_authz.ulid26 DEFAULT NULL)
          RETURNS boolean LANGUAGE sql STABLE AS $fn$
          -- Cache fast path: a row exists for every (user, scope) the user holds an explicit ACL at, plus 'global';
          -- it is rewritten synchronously by recompute_user_perms() inside every role/ACL write. No row → live mask.
          SELECT (COALESCE(
                    (SELECT c.effective FROM forum_authz.effective_perm_cache c
                      WHERE c.user_id = p_user AND c.scope = p_scope
                        AND COALESCE(c.scope_id, '') = COALESCE(p_scope_id, '')),
                    forum_authz.effective_mask(p_user, p_scope, p_scope_id))
                  & COALESCE((SELECT (1 << a.bit) FROM forum_authz.actions a WHERE a.code = p_action), 0)) <> 0;
        $fn$;

        -- The cache is self-invalidating: ANY write to user_roles / acl_entries — the application's stores, the
        -- seeders, an operator's psql — drops the affected users' cache rows, so has_permission() falls back to the
        -- live mask until the next recompute_user_perms(). The SQL stays the live source of truth; callers that
        -- recompute in the same request merely restore the fast path at once.
        CREATE OR REPLACE FUNCTION forum_authz.invalidate_perm_cache_user_roles()
          RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
          IF TG_OP IN ('INSERT', 'UPDATE') THEN
            DELETE FROM forum_authz.effective_perm_cache WHERE user_id = NEW.user_id;
          END IF;
          IF TG_OP IN ('DELETE', 'UPDATE') THEN
            DELETE FROM forum_authz.effective_perm_cache WHERE user_id = OLD.user_id;
          END IF;
          RETURN NULL;
        END;
        $fn$;

        CREATE OR REPLACE FUNCTION forum_authz.invalidate_perm_cache_acl_entries()
          RETURNS trigger LANGUAGE plpgsql AS $fn$
        DECLARE
          r RECORD;
        BEGIN
          FOR r IN
            SELECT NEW.principal_type AS principal_type, NEW.principal_id AS principal_id WHERE TG_OP IN ('INSERT', 'UPDATE')
            UNION
            SELECT OLD.principal_type, OLD.principal_id WHERE TG_OP IN ('DELETE', 'UPDATE')
          LOOP
            IF r.principal_type = 'user' THEN
              DELETE FROM forum_authz.effective_perm_cache WHERE user_id = r.principal_id;
            ELSE
              DELETE FROM forum_authz.effective_perm_cache
              WHERE user_id IN (SELECT ur.user_id FROM forum_authz.user_roles ur WHERE ur.role_id = r.principal_id);
            END IF;
          END LOOP;
          RETURN NULL;
        END;
        $fn$;

        DROP TRIGGER IF EXISTS trg_user_roles_invalidate_perm_cache ON forum_authz.user_roles;
        CREATE TRIGGER trg_user_roles_invalidate_perm_cache
          AFTER INSERT OR UPDATE OR DELETE ON forum_authz.user_roles
          FOR EACH ROW EXECUTE FUNCTION forum_authz.invalidate_perm_cache_user_roles();

        DROP TRIGGER IF EXISTS trg_acl_entries_invalidate_perm_cache ON forum_authz.acl_entries;
        CREATE TRIGGER trg_acl_entries_invalidate_perm_cache
          AFTER INSERT OR UPDATE OR DELETE ON forum_authz.acl_entries
          FOR EACH ROW EXECUTE FUNCTION forum_authz.invalidate_perm_cache_acl_entries();
        """;

    /// <summary>Restores the original (two-pass, cache-less) resolvers and drops the invalidation triggers.</summary>
    public const string OptimizedResolversDown =
        """
        DROP TRIGGER IF EXISTS trg_user_roles_invalidate_perm_cache ON forum_authz.user_roles;
        DROP TRIGGER IF EXISTS trg_acl_entries_invalidate_perm_cache ON forum_authz.acl_entries;
        DROP FUNCTION IF EXISTS forum_authz.invalidate_perm_cache_user_roles();
        DROP FUNCTION IF EXISTS forum_authz.invalidate_perm_cache_acl_entries();

        CREATE OR REPLACE FUNCTION forum_authz.effective_mask(
            p_user  forum_authz.ulid26,
            p_scope text,
            p_scope_id forum_authz.ulid26 DEFAULT NULL)
          RETURNS int LANGUAGE sql STABLE AS $fn$
          WITH role_ids AS (
              SELECT role_id FROM forum_authz.user_roles WHERE user_id = p_user
          ),
          allow AS (
              SELECT forum_authz.int_or_agg(bits) AS m FROM (
                  SELECT allow_bits AS bits FROM forum_authz.roles WHERE role_id IN (SELECT role_id FROM role_ids)
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
        $fn$;

        CREATE OR REPLACE FUNCTION forum_authz.has_permission(
            p_user forum_authz.ulid26, p_action text, p_scope text, p_scope_id forum_authz.ulid26 DEFAULT NULL)
          RETURNS boolean LANGUAGE sql STABLE AS $fn$
          SELECT (forum_authz.effective_mask(p_user, p_scope, p_scope_id)
                  & COALESCE((SELECT (1 << bit) FROM forum_authz.actions WHERE code = p_action), 0)) <> 0;
        $fn$;
        """;
}
