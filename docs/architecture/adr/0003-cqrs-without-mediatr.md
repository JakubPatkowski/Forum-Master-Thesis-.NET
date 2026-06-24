# ADR 0003 — CQRS without MediatR

**Status:** Accepted

**Context.** We want command/query separation and thin endpoints, but also a minimal dependency surface
(security/CVE is a thesis goal) and no licensing entanglements.

**Decision.** Hand-rolled `ICommand/IQuery` + `ICommandHandler/IQueryHandler` (in `Forum.Common`),
auto-registered with **Scrutor** assembly scanning. Handlers return `Result`/`Result<T>` (no exceptions
for expected failures). A single error→HTTP mapper at the edge produces a consistent envelope.

**Consequences.** One less third-party dependency in the hot path; explicit, debuggable dispatch.
We forgo MediatR pipeline behaviours — cross-cutting concerns (validation, logging) are composed explicitly.
