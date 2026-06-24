# ADR 0006 — ULID for every identifier

**Status:** Accepted

**Context.** Identifiers appear in URLs, logs, foreign references and the bus. Sequential integers leak volume
and are trivially enumerable; random GUIDv4 is unsortable and index-unfriendly (random B-tree inserts). The
thesis also needs cross-module references to be opaque and globally unique without coordination.

**Decision.** Use **ULID** (128-bit, Crockford base32, 26 chars) as the primary key of **every** aggregate and
the value of every cross-module reference. Generated in the application (`Ulid` value type), exposed directly in
the public REST API (no separate public id). Stored as a validated text domain `forum.ulid26`
(`^[0-9A-HJKMNP-TV-Z]{26}$`) so values stay sortable, log-readable, and index-friendly; EF maps `Ulid` ↔ that
column. No table uses an integer or GUIDv4 key.

**Consequences.** (+) Time-sortable (good for keyset/feed ordering and BRIN), non-enumerable URLs, generated
client-side without a DB round-trip, stable across modules. (−) 26-char text keys are larger than `bigint`
(slightly bigger indexes) and require the domain/validation; mitigated by the sortability and the avoidance of a
second "public id" column. Contrast with B (gomx), which exposes sequential integers in URLs — a deliberate
divergence we record and (if measured) discuss.
