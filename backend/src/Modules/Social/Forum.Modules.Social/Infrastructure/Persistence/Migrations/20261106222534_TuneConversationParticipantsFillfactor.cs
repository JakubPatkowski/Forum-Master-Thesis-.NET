using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Social.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A/B v5 equal-effort DB tuning (mirrors Architecture B's migration 00025 <c>fillfactor = 90</c>): every sent or
    /// read message UPDATEs the sender's/reader's seat (<c>last_read_on_utc</c>, not indexed) in
    /// <c>conversation_participants</c>; 10 % free space per page keeps those updates HOT. Applies to pages written
    /// from now on; the benchmark reseed rewrites every page.
    /// </summary>
    public partial class TuneConversationParticipantsFillfactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("ALTER TABLE forum_social.conversation_participants SET (fillfactor = 90);");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("ALTER TABLE forum_social.conversation_participants RESET (fillfactor);");
    }
}
