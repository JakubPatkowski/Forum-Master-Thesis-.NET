using Forum.Modules.Identity.Infrastructure.Acl;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forum.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Hot-path cost of the SQL ACL (thesis deviation D10): <c>has_permission()</c> ran the full
    /// <c>effective_mask()</c> on every write (≈ 1.9 ms: two acl_entries passes with a non-sargable
    /// <c>COALESCE(scope_id,'')</c> predicate plus the role joins). This migration keeps the algebra byte-for-byte
    /// (allow = role templates ∪ ACL allow at the scope; deny = ACL deny at the scope; mask = allow &amp; ~deny) but
    /// (1) resolves the scope with an index-friendly predicate and ONE pass over <c>acl_entries</c>, and
    /// (2) lets <c>has_permission()</c> answer from <c>effective_perm_cache</c> when the user has a row for the scope, and
    /// (3) makes the cache self-invalidating: row triggers on <c>user_roles</c> and <c>acl_entries</c> drop the affected
    /// users' cache rows on every insert/update/delete (a role-principal entry drops every holder of the role), so a
    /// direct SQL grant, a seeder batch or an application store write all take effect on the very next check — the
    /// SQL stays the live source of truth. The application stores still call <c>recompute_user_perms</c> in the same
    /// request, which restores the fast path immediately. A missing row (no explicit ACL at that scope, or just
    /// invalidated) falls back to the live computation — correctness never depends on the cache being warm.
    /// </summary>
    public partial class OptimizeEffectiveMask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(AuthzSchema.OptimizedResolversUp);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(AuthzSchema.OptimizedResolversDown);
    }
}
