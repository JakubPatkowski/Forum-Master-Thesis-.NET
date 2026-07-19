"use client";

/**
 * Null-rendering mount point for the presence heartbeat — lives once near the app
 * root (providers.tsx), beside the realtime connection lifecycle.
 */

import { usePresenceHeartbeat } from "@/lib/hooks/use-presence";

export function PresenceHeartbeat() {
  usePresenceHeartbeat();
  return null;
}
