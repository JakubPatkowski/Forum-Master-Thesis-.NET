using Forum.Modules.Social.Infrastructure.Views;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Social.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Raw-SQL read views over forum_social (+ view-level read joins into forum_identity/forum_authz).
    /// The SQL itself lives in <see cref="SocialViews"/>, mirroring Content's AddFtsAndViews.
    /// </summary>
    public partial class AddSocialViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SocialViews.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SocialViews.Down);
        }
    }
}
