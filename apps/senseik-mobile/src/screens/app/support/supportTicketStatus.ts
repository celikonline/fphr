import { SUPPORT_TICKET_STATUSES, type SupportTicketStatus } from "@/services/SupportService";
import type { Tone } from "@/theme/theme";
import i18n from "@/lib/i18n";

/**
 * D3: the backend is mid-migration from PascalCase ("Open") to camelCase ("open") enum
 * serialization for support tickets. Normalize defensively so the badge resolves correctly either
 * way, instead of falling through to `undefined` colors/labels.
 */
export function normalizeTicketStatus(status: string): SupportTicketStatus {
  const camelCase = status.length > 0 ? status.charAt(0).toLowerCase() + status.slice(1) : status;
  return SUPPORT_TICKET_STATUSES.includes(camelCase as SupportTicketStatus)
    ? (camelCase as SupportTicketStatus)
    : "open";
}

/** Ticket status label in the active language. */
export function supportTicketStatusLabel(status: SupportTicketStatus): string {
  return i18n.t(`support:status.${status}`);
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
