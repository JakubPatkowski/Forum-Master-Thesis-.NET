using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Content.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "forum_content");

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "forum_content",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    icon_file_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    last_modified_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "forum_content",
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
                name: "tags",
                schema: "forum_content",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    slug = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "threads",
                schema: "forum_content",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    category_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    last_modified_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_threads", x => x.id);
                    table.ForeignKey(
                        name: "fk_threads_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "forum_content",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "comments",
                schema: "forum_content",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    thread_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    parent_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    owner_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    path = table.Column<string>(type: "character varying(161)", unicode: false, maxLength: 161, nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    last_modified_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_comments_comments_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "forum_content",
                        principalTable: "comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_comments_threads_thread_id",
                        column: x => x.thread_id,
                        principalSchema: "forum_content",
                        principalTable: "threads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thread_tags",
                schema: "forum_content",
                columns: table => new
                {
                    thread_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    tag_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_thread_tags", x => new { x.thread_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_thread_tags_tags_tag_id",
                        column: x => x.tag_id,
                        principalSchema: "forum_content",
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_thread_tags_threads_thread_id",
                        column: x => x.thread_id,
                        principalSchema: "forum_content",
                        principalTable: "threads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_categories_slug",
                schema: "forum_content",
                table: "categories",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comments_parent_id",
                schema: "forum_content",
                table: "comments",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_comments_thread_path",
                schema: "forum_content",
                table: "comments",
                columns: new[] { "thread_id", "path" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_on_utc",
                schema: "forum_content",
                table: "outbox_messages",
                column: "processed_on_utc");

            migrationBuilder.CreateIndex(
                name: "ix_tags_slug",
                schema: "forum_content",
                table: "tags",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_thread_tags_tag_id",
                schema: "forum_content",
                table: "thread_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_threads_feed",
                schema: "forum_content",
                table: "threads",
                columns: new[] { "category_id", "is_pinned", "created_on_utc", "id" },
                descending: new[] { false, true, true, true },
                filter: "is_deleted = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comments",
                schema: "forum_content");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "forum_content");

            migrationBuilder.DropTable(
                name: "thread_tags",
                schema: "forum_content");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "forum_content");

            migrationBuilder.DropTable(
                name: "threads",
                schema: "forum_content");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "forum_content");
        }
    }
}
