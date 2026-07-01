using Forum.Modules.Identity.Infrastructure.Acl;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthzSchema : Migration
    {
        // The RBAC + bitmask-ACL schema ships as raw SQL (ADR 0004), not code-first EF entities.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(AuthzSchema.Up);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(AuthzSchema.Down);
    }
}
