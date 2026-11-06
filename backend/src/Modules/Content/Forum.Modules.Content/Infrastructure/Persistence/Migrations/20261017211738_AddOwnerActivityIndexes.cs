using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Content.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerActivityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_threads_owner_activity",
                schema: "forum_content",
                table: "threads",
                columns: new[] { "owner_id", "created_on_utc", "id" },
                descending: new[] { false, true, true },
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_comments_owner_activity",
                schema: "forum_content",
                table: "comments",
                columns: new[] { "owner_id", "created_on_utc", "id" },
                descending: new[] { false, true, true },
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_threads_owner_activity",
                schema: "forum_content",
                table: "threads");

            migrationBuilder.DropIndex(
                name: "ix_comments_owner_activity",
                schema: "forum_content",
                table: "comments");
        }
    }
}
