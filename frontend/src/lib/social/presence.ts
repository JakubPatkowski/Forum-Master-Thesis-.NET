/**
 * Presence derivation helpers. Presence is poll-only (deliberately never on the
 * realtime bus): views batch every visible userId into ONE GET /api/social/presence
 * call and derive row state from the returned entries via these helpers.
 */

import type { PresenceEntry, PresenceStatus } from "@/lib/api/types";

export function presenceMap(entries: PresenceEntry[] | undefined): Map<string, PresenceStatus> {
  return new Map((entries ?? []).map((entry) => [entry.userId, entry.status]));
}

/** Unknown/not-yet-fetched users read as offline — never as a false "online". */
export function statusOf(map: Map<string, PresenceStatus>, userId: string): PresenceStatus {
  return map.get(userId) ?? "offline";
}

/** The LiveDot color semantics the design uses for presence. */
export function presenceDotColor(status: PresenceStatus): "green" | "amber" | "red" {
  return status === "online" ? "green" : status === "away" ? "amber" : "red";
}

export function presenceLabel(status: PresenceStatus): string {
  return status === "online" ? "Active now" : status === "away" ? "Away" : "Offline";
}
