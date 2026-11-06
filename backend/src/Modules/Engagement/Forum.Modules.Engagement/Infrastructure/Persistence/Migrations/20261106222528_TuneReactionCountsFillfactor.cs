using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Engagement.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A/B v5 equal-effort DB tuning (mirrors Architecture B's migration 00025 <c>fillfactor = 90</c> on its
    /// counter-carrying tables): every reaction toggle UPDATEs the row of <c>reaction_counts</c> through the
    /// <c>trg_reactions_count</c> trigger, and <c>reaction_count</c> is not indexed, so with 10 % free space per page
    /// most of those updates stay HOT (heap-only, no new index entry — <c>pg_stat_user_tables.n_tup_hot_upd</c>).
    /// Applies to pages written from now on; the benchmark reseed (TRUNCATE + reinsert) rewrites every page.
    /// </summary>
    public partial class TuneReactionCountsFillfactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("ALTER TABLE forum_engagement.reaction_counts SET (fillfactor = 90);");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("ALTER TABLE forum_engagement.reaction_counts RESET (fillfactor);");
    }
}
