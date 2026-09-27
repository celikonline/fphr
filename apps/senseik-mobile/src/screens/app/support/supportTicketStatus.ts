import { TICKET_STATUS_LABELS, type SupportTicketStatus } from "@/services/SupportService";
import type { Tone } from "@/theme/theme";

/**
 * D3: the backend is mid-migration from PascalCase ("Open") to camelCase ("open") enum
 * serialization for support tickets. Normalize defensively so the badge resolves correctly either
 * way, instead of falling through to `undefined` colors/labels.
 */
export function normalizeTicketStatus(status: string): SupportTicketStatus {
  const camelCase = status.length > 0 ? status.charAt(0).toLowerCase() + status.slice(1) : status;
  return camelCase in TICKET_STATUS_LABELS ? (camelCase as SupportTicketStatus) : "open";
}

/** Maps a normalized ticket status to the shared `StatusPill` tone palette. */
export function supportTicketStatusTone(status: SupportTicketStatus): Tone {
  switch (status) {
    case "answered":
      return "success";
    case "closed":
      return "neutral";
    case "open":
    default:
      return "warning";
  }
}
