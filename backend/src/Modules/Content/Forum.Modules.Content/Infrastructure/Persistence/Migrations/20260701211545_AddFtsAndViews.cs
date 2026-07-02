using Forum.Modules.Content.Infrastructure.Fts;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Content.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFtsAndViews : Migration
    {
        // The FTS column/trigger/GIN index and the read views ship as raw SQL, outside the EF model.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(ContentFtsAndViews.Up);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(ContentFtsAndViews.Down);
    }
}
