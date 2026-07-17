using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Social.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSocial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "forum_social");

            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "forum_social",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", unicode: false, maxLength: 16, nullable: false),
                    direct_key = table.Column<string>(type: "character varying(53)", unicode: false, maxLength: 53, nullable: true),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    last_modified_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "friendships",
                schema: "forum_social",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    requester_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    addressee_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", unicode: false, maxLength: 16, nullable: false),
                    accepted_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    last_modified_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_friendships", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "groups",
                schema: "forum_social",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    visibility = table.Column<string>(type: "character varying(16)", unicode: false, maxLength: 16, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
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
                    table.PrimaryKey("pk_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "forum_social",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    processed_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "forum_social",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    user_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    target_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "forum_social",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    processed_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "social_blocks",
                schema: "forum_social",
                columns: table => new
                {
                    blocker_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    blocked_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_social_blocks", x => new { x.blocker_id, x.blocked_id });
                });

            migrationBuilder.CreateTable(
                name: "user_presence",
                schema: "forum_social",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    last_heartbeat_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_presence", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "user_privacy_settings",
                schema: "forum_social",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    friend_requests = table.Column<string>(type: "character varying(16)", unicode: false, maxLength: 16, nullable: false),
                    messages = table.Column<string>(type: "character varying(16)", unicode: false, maxLength: 16, nullable: false),
                    group_invites = table.Column<string>(type: "character varying(16)", unicode: false, maxLength: 16, nullable: false),
                    show_online_status = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_privacy_settings", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "conversation_participants",
                schema: "forum_social",
                columns: table => new
                {
                    conversation_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    user_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    joined_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    left_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_read_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_muted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_participants", x => new { x.conversation_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_conversation_participants_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "forum_social",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "forum_social",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    conversation_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    edited_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "forum_social",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_invites",
                schema: "forum_social",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    group_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    invited_user_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    invited_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    created_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_modified_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true),
                    last_modified_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_invites", x => x.id);
                    table.ForeignKey(
                        name: "fk_group_invites_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "forum_social",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_memberships",
                schema: "forum_social",
                columns: table => new
                {
                    group_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    user_id = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: false),
                    joined_on_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    invited_by = table.Column<string>(type: "character varying(26)", unicode: false, maxLength: 26, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_memberships", x => new { x.group_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_group_memberships_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "forum_social",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_participants_user",
                schema: "forum_social",
                table: "conversation_participants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_conversations_direct_key",
                schema: "forum_social",
                table: "conversations",
                column: "direct_key",
                unique: true,
                filter: "direct_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_friendships_addressee",
                schema: "forum_social",
                table: "friendships",
                column: "addressee_id");

            migrationBuilder.CreateIndex(
                name: "ix_friendships_requester",
                schema: "forum_social",
                table: "friendships",
                column: "requester_id");

            migrationBuilder.CreateIndex(
                name: "ux_friendships_pair_directed",
                schema: "forum_social",
                table: "friendships",
                columns: new[] { "requester_id", "addressee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_invites_invitee",
                schema: "forum_social",
                table: "group_invites",
                column: "invited_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_invites_inviter",
                schema: "forum_social",
                table: "group_invites",
                column: "invited_by");

            migrationBuilder.CreateIndex(
                name: "ux_group_invites_pending",
                schema: "forum_social",
                table: "group_invites",
                columns: new[] { "group_id", "invited_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_memberships_user",
                schema: "forum_social",
                table: "group_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_groups_owner",
                schema: "forum_social",
                table: "groups",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_groups_public_directory",
                schema: "forum_social",
                table: "groups",
                column: "id",
                descending: new bool[0],
                filter: "visibility = 'public' AND is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_messages_history",
                schema: "forum_social",
                table: "messages",
                columns: new[] { "conversation_id", "id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_feed",
                schema: "forum_social",
                table: "notifications",
                columns: new[] { "user_id", "id" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_unread",
                schema: "forum_social",
                table: "notifications",
                column: "user_id",
                filter: "is_read = false");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_on_utc",
                schema: "forum_social",
                table: "outbox_messages",
                column: "processed_on_utc");

            migrationBuilder.CreateIndex(
                name: "ix_social_blocks_blocked",
                schema: "forum_social",
                table: "social_blocks",
                column: "blocked_id");

            // One friendship row per UNORDERED pair: the EF unique index above only covers the directed pair,
            // so a simultaneous A->B and B->A request could otherwise both land. Expression indexes are not
            // modelable in EF -- raw SQL, same as Content's FTS artifacts.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ux_friendships_pair ON forum_social.friendships
                    (LEAST(requester_id, addressee_id), GREATEST(requester_id, addressee_id));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_participants",
                schema: "forum_social");

            migrationBuilder.DropTable(
                name: "friendships",
                schema: "forum_social");

            migrationBuilder.DropTable(
                name: "group_invites",
                schema: "forum_social");

            migrationBuilder.DropTable(
                name: "group_memberships",
                schema: "forum_social");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "forum_social");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "forum_social");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "forum_social");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "forum_social");

            migrationBuilder.DropTable(
                name: "social_blocks",
                schema: "forum_social");

            migrationBuilder.DropTable(
                name: "user_presence",
                schema: "forum_social");

            migrationBuilder.DropTable(
                name: "user_privacy_settings",
                schema: "forum_social");

            migrationBuilder.DropTable(
                name: "groups",
                schema: "forum_social");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "forum_social");
        }
    }
}
