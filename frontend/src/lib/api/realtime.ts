import { apiFetch } from "@/lib/api/http";
import type { RealtimeTicketResponse } from "@/lib/api/types";

export const realtimeApi = {
  /**
   * Mints a single-use, 30-second WS ticket. Always call this immediately before
   * opening the socket — never cache or reuse a ticket (brief §4.9).
   */
  createTicket: () => apiFetch<RealtimeTicketResponse>("/api/realtime/ticket", { method: "POST" }),
};
