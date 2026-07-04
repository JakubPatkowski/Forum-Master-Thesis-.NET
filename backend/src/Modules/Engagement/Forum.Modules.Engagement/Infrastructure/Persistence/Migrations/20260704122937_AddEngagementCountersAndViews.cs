using Forum.Modules.Engagement.Infrastructure.Counters;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Engagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEngagementCountersAndViews : Migration
    {
        // The trigger-maintained reaction_counts table and the user_stats_v view ship as raw SQL, outside the EF model.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EngagementCountersAndViews.Up);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EngagementCountersAndViews.Down);
    }
}
