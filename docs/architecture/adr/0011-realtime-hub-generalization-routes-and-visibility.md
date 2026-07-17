# ADR 0011 — Realtime hub generalization: routes-as-data + per-kind visibility

**Status:** Accepted (2026-07-17, Phase 11 — Social)

**Context.** The Phase 7 hub (ADR 0010) was built for exactly one producer pair: Content + Engagement. Its
internals hardcoded that shape — `RealtimeNotification` carried `(CategoryId, ThreadId, ActorUserId)`,
`SubscriptionSet.Matches` knew that "a category subscription matches the notification's CategoryId", and the
dispatcher resolved exactly one visibility port (`IContentVisibility`, category scope) for every push. The Social
module adds events whose routing (friend pairs, group members, conversation participants, a single bell
recipient) and whose authorization rule (participant-of-conversation, not owner-or-moderate-at-category) simply
do not fit those field names. Bolting `ConversationId`/`GroupId` onto the same record would have made every
future module a third wart.

**Decision.** Two orthogonal generalizations, both data-driven:

1. **Routes as data.** `RealtimeNotification` becomes
   `(ChangeNotification Payload, RealtimeVisibility Visibility, IReadOnlyList<SubscriptionView> Routes)`.
   `RealtimeEventMap` — already the single place events are interpreted — now BUILDS the routes
   (e.g. a comment → `[category:C, thread:T]`; a DM → `[conversation:X, user:A, user:B]`; a bell ping →
   `[user:U]`). Matching collapses to pure set intersection (`SubscriptionSet.MatchesAny(routes)`): the
   subscription set no longer knows what any view kind *means*. New view kinds (`group`, `conversation`) are one
   enum member + one parse case.

2. **Visibility as a closed per-kind strategy.** The dispatcher switches on `RealtimeVisibility.Kind`:
   - `Category(id)` → `IContentVisibility` exactly as before (vanished category ⇒ push to nobody; private ⇒
     per-subscriber owner-or-`moderate` at category scope);
   - `Conversation(id)` → Social's `ISocialVisibility.IsConversationParticipantAsync` per subscriber — ONE rule
     covers DMs *and* every group-scoped event, because a group chat's conversation id IS the group id and
     membership changes write through to `conversation_participants` in the same transaction;
   - `TargetUsers` → no per-push check at all, valid ONLY because such notifications route exclusively to `user`
     views and the socket protocol gates `user` subscriptions to self at subscribe time.

**Subscribe-time authorization stays as ADR 0010 set it:** category/thread/group/conversation subscriptions are
always accepted and authorization happens on every push (memberships change mid-connection; one code path), the
`user` view alone is subscribe-time self-gated — which is precisely what makes `TargetUsers` sound.

**The wire envelope did NOT change.** `{type, entity, id, parentId?, categoryId?}` is a frontend contract;
Social events set `categoryId = null` and use `parentId` as their container (conversation for messages, group
for invites/members). New `entity` values: `friendship`, `group`, `group_member`, `group_invite`, `message`,
`notification`.

**Consequences.**
- A future module feeds the hub by adding switch arms in `RealtimeEventMap` + (at most) one visibility strategy —
  no changes to matching, registry or socket handling.
- Per-subscriber `Conversation` checks are one indexed PK lookup each, mirroring the accepted cost of the
  private-category checks; both remain bounded by the subscriber count of a single replica.
- Presence is deliberately NOT on the bus (REQUIREMENTS §1: unmeasured, A-only) — it stays a REST
  heartbeat + batch read behind `IPresenceStore`, the seam the scoped Redis session swaps.
