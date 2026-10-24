using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Content.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A/B parity (deviation D2 of the thesis comparison): feed, search and profile cards carry a 300-character
    /// plain-text excerpt of the body, the same length Architecture B's cards render under
    /// <c>GOMX_CARD_PREVIEW_CHARS=300</c>. The excerpt is a SQL function applied AFTER the keyset LIMIT (PK join in
    /// <c>ContentQueries</c>), so a page costs 20 excerpts — never one per scanned row — and the view stays as is.
    /// </summary>
    public partial class AddFeedExcerpt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- Plain-text excerpt of a markdown body: strip the common markdown punctuation, collapse whitespace,
                -- cut at 300 characters. IMMUTABLE + PARALLEL SAFE so the planner may inline/cache it.
                CREATE OR REPLACE FUNCTION forum_content.thread_excerpt(body text)
                  RETURNS text LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT AS $fn$
                  SELECT left(btrim(regexp_replace(regexp_replace($1, '[#*_`~>!|\[\]()]+', ' ', 'g'), '\s+', ' ', 'g')), 300)
                $fn$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS forum_content.thread_excerpt(text);");
        }
    }
}
