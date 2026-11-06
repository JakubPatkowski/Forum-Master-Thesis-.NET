using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Content.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A/B v5 equal-effort DB tuning on the search path (mirrors Architecture B's migrations 00025/00027):
    /// <list type="bullet">
    /// <item><c>ix_threads_newest</c> — the keyset index behind <c>search?sort=newest</c> (created_on_utc DESC, id DESC
    /// over live rows): with a custom plan the planner walks it and stops after one page for a common word instead of
    /// ranking every match.</item>
    /// <item><c>fastupdate = off</c> on the FTS GIN index: Postgres adds the pending-list pages to the index's cost, so
    /// as the list filled between vacuums search drifted towards sequential scans and a rare word cost as much as the
    /// most common one; inserts and non-HOT updates now pay the entry cost up front. The pending entries that exist at
    /// migration time are flushed, otherwise they would still be scanned.</item>
    /// </list>
    /// </summary>
    public partial class TuneSearchNewestIndexAndGin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_threads_newest",
                schema: "forum_content",
                table: "threads",
                columns: new[] { "created_on_utc", "id" },
                descending: new bool[0],
                filter: "is_deleted = false");

            migrationBuilder.Sql(
                """
                ALTER INDEX forum_content.ix_threads_search SET (fastupdate = off);
                SELECT gin_clean_pending_list('forum_content.ix_threads_search'::regclass);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER INDEX forum_content.ix_threads_search RESET (fastupdate);");

            migrationBuilder.DropIndex(
                name: "ix_threads_newest",
                schema: "forum_content",
                table: "threads");
        }
    }
}
