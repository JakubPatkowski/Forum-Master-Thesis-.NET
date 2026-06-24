# ADR 0004 — SQL-resolved bitmask ACL

**Status:** Accepted

**Context.** RBAC + per-object ACL with `effective = (∪ roles) ∪ grants \ denies`. Computing this in C#
costs multiple queries per check.

**Decision.** Adopt a proven production pattern: integer **bitmask** permissions, a custom Postgres
`int_or_agg` aggregate, **SQL resolver functions**, and an **`effective_perm_cache`** with partial + BRIN
indexes. Full design in `docs/db/permissions-acl-design.md`. Shipped as raw-SQL EF migrations, applied by the
migration Job.

**Consequences.** O(1) permission checks, set-algebra in the engine, auditable in SQL. Cost: SQL the team must
maintain, and cache invalidation on role/ACL change (driven by domain events).
