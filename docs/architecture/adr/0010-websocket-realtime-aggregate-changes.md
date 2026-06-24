# ADR 0010 — WebSocket real-time on aggregate change (fetch-then-patch)

**Status:** Accepted

**Context.** The SPA must reflect new/changed threads, comments and reactions live, without polling. We already
emit integration events on RabbitMQ (ADR 0009); we want the frontend to load a view once and then update only
the parts that change.

**Decision.** A **WebSocket fan-out** in `Forum.Api` subscribes to the relevant RabbitMQ integration events and
relays a **compact change notification** to connected clients:

```
{ "type": "created|updated|deleted",
  "entity": "thread|comment|reaction",
  "id": "<ulid>", "parentId": "<ulid>?", "categoryId": "<ulid>?", "version": <n> }
```

- The notification carries **identity + routing, not the full entity** — the client decides whether to patch from
  the payload or re-fetch the affected resource (keeps payloads small and avoids leaking fields a viewer may not
  see).
- **Targeting/scoping:** broadcasts are scoped (per category / thread / user) so a client only receives what its
  current view subscribes to; **private-category** changes are never pushed to non-members (authorization is
  re-checked server-side).
- **Frontend pattern:** the SPA fetches everything it needs on navigation (REST + React Query), opens **one**
  WebSocket, and on each notification calls `invalidateQueries`/`setQueryData` for the affected key — only the
  touched component re-renders. On (re)connect it resyncs (re-fetches the current view), so missed events while
  disconnected self-heal.
- **Scale-out:** because the source is the bus, **every replica** receives every relevant event and pushes to its
  own connected sockets; any pod can serve any socket. No sticky sessions required for correctness.

**Consequences.** (+) Live updates with minimal bandwidth; the SPA's cache stays the single source of truth on the
client; reuses the existing event backbone (no second mechanism); horizontally scalable. (−) The client needs
reconnect/resync logic and per-view subscription management; the hub must enforce visibility on every push. This
mirrors B's live model (HTML fragments over WS) but A pushes **change notifications to a JSON/React cache** instead
of server-rendered HTML — a core architectural contrast for the thesis.
