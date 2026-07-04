namespace Forum.Modules.Engagement.Infrastructure.Counters;

/// <summary>
/// The raw SQL for the Engagement read models, all outside the EF model on purpose:
/// <c>reaction_counts</c> holds the denormalized per-(target, kind) tallies and the row trigger is its ONLY
/// writer — the counter can never drift from the reactions table no matter which code path inserts or deletes
/// (toggle handlers, deletion-cascade consumers, future seeding), and feed count reads are O(1) primary-key
/// lookups instead of aggregate scans (the scalability talking point vs architecture B). Zeroed tallies are
/// removed so the table stays exactly the set of currently-reacted targets.
/// <c>user_stats_v</c> is a READ-ONLY cross-schema view — Engagement migrates last (module registration
/// order), so <c>forum_identity.users</c> and <c>forum_content.threads/comments</c> already exist; same
/// view-level join precedent as Content's views over <c>forum_identity.users</c>. Karma is the signed sum of
/// reaction values received on the user's live content ('like' = +1 today; a future downvote lands as -1
/// without touching this view).
/// </summary>
internal static class EngagementCountersAndViews
{
    public const string Up =
        """
        CREATE TABLE forum_engagement.reaction_counts (
          target_type    character varying(16) NOT NULL,
          target_id      character varying(26) NOT NULL,
          reaction_type  character varying(32) NOT NULL,
          reaction_count integer NOT NULL DEFAULT 0,
          CONSTRAINT pk_reaction_counts PRIMARY KEY (target_type, target_id, reaction_type)
        );

        CREATE FUNCTION forum_engagement.reactions_count_update()
        RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
          IF TG_OP = 'INSERT' THEN
            INSERT INTO forum_engagement.reaction_counts (target_type, target_id, reaction_type, reaction_count)
            VALUES (NEW.target_type, NEW.target_id, NEW.reaction_type, 1)
            ON CONFLICT (target_type, target_id, reaction_type)
            DO UPDATE SET reaction_count = reaction_counts.reaction_count + 1;
            RETURN NEW;
          END IF;

          UPDATE forum_engagement.reaction_counts
             SET reaction_count = reaction_count - 1
           WHERE target_type = OLD.target_type AND target_id = OLD.target_id
             AND reaction_type = OLD.reaction_type;
          DELETE FROM forum_engagement.reaction_counts
           WHERE target_type = OLD.target_type AND target_id = OLD.target_id
             AND reaction_type = OLD.reaction_type AND reaction_count <= 0;
          RETURN OLD;
        END;
        $fn$;

        CREATE TRIGGER trg_reactions_count
          AFTER INSERT OR DELETE ON forum_engagement.reactions
          FOR EACH ROW EXECUTE FUNCTION forum_engagement.reactions_count_update();

        -- Per-user public stats; counts exclude soft-deleted content, karma counts only reactions whose
        -- target is still live (the deletion cascade removes the rest asynchronously anyway).
        CREATE VIEW forum_engagement.user_stats_v AS
        SELECT
          u.id AS user_id,
          u.username,
          u.display_name,
          (SELECT count(*)::int FROM forum_content.threads t
            WHERE t.owner_id = u.id AND t.is_deleted = false) AS thread_count,
          (SELECT count(*)::int FROM forum_content.comments cm
            WHERE cm.owner_id = u.id AND cm.is_deleted = false) AS comment_count,
          (COALESCE((SELECT sum(r.value) FROM forum_engagement.reactions r
                      JOIN forum_content.threads t ON t.id = r.target_id
                     WHERE r.target_type = 'thread' AND t.owner_id = u.id AND t.is_deleted = false), 0)
           + COALESCE((SELECT sum(r.value) FROM forum_engagement.reactions r
                        JOIN forum_content.comments cm ON cm.id = r.target_id
                       WHERE r.target_type = 'comment' AND cm.owner_id = u.id AND cm.is_deleted = false), 0)
          )::int AS karma
        FROM forum_identity.users u;
        """;

    public const string Down =
        """
        DROP VIEW IF EXISTS forum_engagement.user_stats_v;
        DROP TRIGGER IF EXISTS trg_reactions_count ON forum_engagement.reactions;
        DROP FUNCTION IF EXISTS forum_engagement.reactions_count_update();
        DROP TABLE IF EXISTS forum_engagement.reaction_counts;
        """;
}
