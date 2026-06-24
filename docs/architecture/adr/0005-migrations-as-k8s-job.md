# ADR 0005 — Migrations as a Kubernetes Job

**Status:** Accepted

**Context.** A common approach applies EF migrations at app startup. With an HPA (multiple replicas) that races
and risks partial schema changes.

**Decision.** Schema migrations + SQL view/function (re)creation run as a one-shot **Job** that must complete
before the Deployment rolls. The Host supports a `migrate` entrypoint arg. Locally, a startup task may apply
migrations for convenience, gated by configuration.

**Consequences.** Safe, idempotent, observable migrations independent of replica count. Deploy scripts wait on
the Job (`kubectl wait --for=condition=complete`).
