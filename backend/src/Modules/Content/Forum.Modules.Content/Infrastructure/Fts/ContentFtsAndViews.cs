namespace Forum.Modules.Content.Infrastructure.Fts;

/// <summary>
/// The raw SQL for full-text search and the Content read models: the trigger-maintained <c>search_tsv</c>
/// column (+ GIN index) and the <c>thread_feed_v</c> / <c>comment_tree_v</c> / <c>thread_detail_v</c> views.
/// All of it lives outside the EF model on purpose: EF never writes <c>search_tsv</c> and never diffs the views.
/// The views JOIN <c>forum_identity.users</c> — a view-level read join, not a cross-schema FK; the Identity
/// migrations run first (module registration order), so the table exists by the time these views are created.
/// </summary>
internal static class ContentFtsAndViews
{
    public const string Up =
        """
        -- FTS: title weighs A, body weighs B; the trigger is the only writer of search_tsv.
        ALTER TABLE forum_content.threads ADD COLUMN search_tsv tsvector;

        CREATE OR REPLACE FUNCTION forum_content.threads_search_update()
        RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
          NEW.search_tsv :=
            setweight(to_tsvector('simple', coalesce(NEW.title,'')), 'A') ||
            setweight(to_tsvector('simple', coalesce(NEW.body,'')), 'B');
          RETURN NEW;
        END;
        $fn$;

        CREATE TRIGGER trg_threads_search
          BEFORE INSERT OR UPDATE OF title, body ON forum_content.threads
          FOR EACH ROW EXECUTE FUNCTION forum_content.threads_search_update();

        -- Backfill any rows that predate the trigger (no-op on a fresh database).
        UPDATE forum_content.threads SET search_tsv =
          setweight(to_tsvector('simple', coalesce(title,'')), 'A') ||
          setweight(to_tsvector('simple', coalesce(body,'')), 'B');

        CREATE INDEX ix_threads_search ON forum_content.threads USING gin (search_tsv);

        -- Feed read model: live threads + author display + category; counts become real in Phase 4 (Engagement).
        CREATE VIEW forum_content.thread_feed_v AS
        SELECT
          t.id, t.category_id, t.title, t.is_pinned, t.is_deleted,
          t.created_on_utc, t.last_modified_on_utc,
          t.owner_id,
          u.username, u.display_name,
          c.slug AS category_slug, c.name AS category_name,
          0 AS like_count,
          0 AS comment_count,
          t.search_tsv
        FROM forum_content.threads t
        JOIN forum_identity.users u ON u.id = t.owner_id
        JOIN forum_content.categories c ON c.id = t.category_id
        WHERE t.is_deleted = false;

        -- Comment tree read model: soft-deleted rows ARE included (their body is already "[deleted]"),
        -- so children keep an anchor; ORDER BY (thread_id, path) reads the tree depth-first.
        CREATE VIEW forum_content.comment_tree_v AS
        SELECT
          cm.id, cm.thread_id, cm.parent_id, cm.path, cm.depth,
          cm.body, cm.is_deleted, cm.created_on_utc,
          cm.owner_id, u.username, u.display_name
        FROM forum_content.comments cm
        JOIN forum_identity.users u ON u.id = cm.owner_id;

        -- Single-thread read model: full body + author + category + aggregated tag slugs.
        CREATE VIEW forum_content.thread_detail_v AS
        SELECT
          t.id, t.category_id, c.slug AS category_slug, c.name AS category_name,
          t.title, t.body, t.is_pinned,
          t.owner_id, u.username, u.display_name,
          (SELECT COALESCE(array_agg(tg.slug::text ORDER BY tg.slug), ARRAY[]::text[])
             FROM forum_content.thread_tags tt
             JOIN forum_content.tags tg ON tg.id = tt.tag_id
            WHERE tt.thread_id = t.id) AS tag_slugs,
          t.created_on_utc, t.last_modified_on_utc
        FROM forum_content.threads t
        JOIN forum_identity.users u ON u.id = t.owner_id
        JOIN forum_content.categories c ON c.id = t.category_id
        WHERE t.is_deleted = false;
        """;

    public const string Down =
        """
        DROP VIEW IF EXISTS forum_content.thread_detail_v;
        DROP VIEW IF EXISTS forum_content.comment_tree_v;
        DROP VIEW IF EXISTS forum_content.thread_feed_v;
        DROP INDEX IF EXISTS forum_content.ix_threads_search;
        DROP TRIGGER IF EXISTS trg_threads_search ON forum_content.threads;
        DROP FUNCTION IF EXISTS forum_content.threads_search_update();
        ALTER TABLE forum_content.threads DROP COLUMN IF EXISTS search_tsv;
        """;
}
