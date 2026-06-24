# ADR 0009 — RabbitMQ integration events between modules (transactional outbox)

**Status:** Accepted

**Context.** In a modular monolith, modules must stay decoupled: no module may reach into another's internals or
schema. They still need to react to each other (Files attaches on `FileCommitted`; Engagement cascades on
`ThreadDeleted`; the WebSocket hub fans out on every change). Synchronous in-process calls would re-couple them
and lose delivery guarantees across restarts.

**Decision.** Two event tiers:

- **Domain events** stay *inside* a module: an aggregate `Raise(...)`s them; they are dispatched in
  `SaveChangesAndDispatchEventsAsync` to in-module handlers. Never leave the module.
- **Integration events** cross module boundaries over **RabbitMQ**, delivered with a **transactional outbox**:
  the event is written to the source module's `outbox_messages` table **in the same DB transaction** as the
  state change; a background relay publishes unprocessed rows to a **topic exchange** (one per source module,
  routing key = event name) and marks them processed. Consumers live in their own module, bind their queues to
  the exchanges they care about, and are **idempotent** (dedupe by `EventId` in an inbox table). Contracts are
  immutable records in `*.Contracts` with a stable, versioned name, `EventId` (ULID), `OccurredOnUtc`, and a
  `CorrelationId` propagated from the originating request.

The event catalog (publishers/consumers) is in `REQUIREMENTS-AND-ASSUMPTIONS.md` §2.

**Consequences.** (+) Modules decoupled and independently testable; **at-least-once** delivery survives consumer
downtime; the WebSocket fan-out is just another consumer (ADR 0010); outbox keeps publish atomic with the write
(no "saved but not published" gap). (−) Eventual consistency across modules; operational surface (RabbitMQ +
relay + dedupe); consumers must be idempotent and tolerate reordering. Readiness gates on RabbitMQ. This is the
deliberate "professional/over-engineered" tier the thesis showcases; B uses Redis pub/sub (ephemeral) instead —
a recorded divergence.
