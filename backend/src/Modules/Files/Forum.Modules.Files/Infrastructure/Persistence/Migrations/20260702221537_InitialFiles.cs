using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Files.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "forum_files");

            migrationBuilder.CreateTable(
                name: "files",
                schema: "forum_files",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    bucket = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    object_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    uploaded_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    committed_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    last_modified_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "forum_files",
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
                name: "file_attachments",
                schema: "forum_files",
                columns: table => new
                {
                    file_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    target_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_attachments", x => new { x.file_id, x.target_type, x.target_id });
                    table.ForeignKey(
                        name: "fk_file_attachments_files_file_id",
                        column: x => x.file_id,
                        principalSchema: "forum_files",
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attach_target",
                schema: "forum_files",
                table: "file_attachments",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_files_bucket_object_key",
                schema: "forum_files",
                table: "files",
                columns: new[] { "bucket", "object_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_files_committed_sweep",
                schema: "forum_files",
                table: "files",
                column: "committed_on_utc",
                filter: "status = 'committed'");

            migrationBuilder.CreateIndex(
                name: "ix_files_pending_sweep",
                schema: "forum_files",
                table: "files",
                column: "created_on_utc",
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_on_utc",
                schema: "forum_files",
                table: "outbox_messages",
                column: "processed_on_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_attachments",
                schema: "forum_files");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "forum_files");

            migrationBuilder.DropTable(
                name: "files",
                schema: "forum_files");
        }
    }
}
