import { humanizeLeaveTypeCode, leaveTypeDisplayName } from "@/lib/leaveTypeNames";
import type { InboxTaskLeaveSummary } from "@/services/ApprovalsService";

/** `RequestType` (Workflow.Domain) server value - same mapping as web `workflow.json`'daki `requestType.*`. */
const REQUEST_TYPE_LABELS: Record<string, string> = {
  Leave: "İzin",
  Advance: "Avans",
  Expense: "Harcama",
  Overtime: "Fazla mesai",
  Requisition: "Kadro talebi",
  AssetAssignment: "Zimmet",
};

export function requestTypeLabel(requestType: string): string {
  return REQUEST_TYPE_LABELS[requestType] ?? requestType;
}

/** `TimeOnly` ("HH:mm:ss") → "HH:mm" for display. */
export function formatTime(value: string): string {
  return value.slice(0, 5);
}

/**
 * B5: the inbox summary's `leaveTypeName` can be a raw seeded resource key (e.g. "leave.type.annual")
 * just like `LeaveService`'s balances/requestable-types responses - translate it the same way. When
 * it's missing entirely, fall back to a humanized `leaveTypeCode` instead of a bare/raw string.
 */
export function leaveSummaryTypeLabel(
  leave: Pick<InboxTaskLeaveSummary, "leaveTypeName" | "leaveTypeCode">
): string {
  if (leave.leaveTypeName && leave.leaveTypeName.trim().length > 0) {
    return leaveTypeDisplayName(leave.leaveTypeName);
  }
  return humanizeLeaveTypeCode(leave.leaveTypeCode);
}
