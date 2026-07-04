using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Engagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialEngagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "forum_engagement");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "forum_engagement",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reactions",
                schema: "forum_engagement",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    target_type = table.Column<string>(type: "character varying(16)", unicode: false, maxLength: 16, nullable: false),
                    target_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    reaction_type = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false),
                    value = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reactions", x => new { x.user_id, x.target_type, x.target_id, x.reaction_type });
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_on_utc",
                schema: "forum_engagement",
                table: "outbox_messages",
                column: "processed_on_utc");

            migrationBuilder.CreateIndex(
                name: "ix_reactions_target",
                schema: "forum_engagement",
                table: "reactions",
                columns: new[] { "target_type", "target_id", "reaction_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "forum_engagement");

            migrationBuilder.DropTable(
                name: "reactions",
                schema: "forum_engagement");
        }
    }
}
