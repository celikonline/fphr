import { humanizeLeaveTypeCode, leaveTypeDisplayName } from "@/lib/leaveTypeNames";
import type { InboxTaskLeaveSummary } from "@/services/ApprovalsService";

/** The four request types the "Tür" filter (spec §5) offers - also the ones the card's amount/
 *  duration heading knows how to compute (see `approvalPresentation.ts`). */
export type ApprovalTypeKey = "leave" | "overtime" | "expense" | "advance";

/**
 * `InboxTask.requestType` server values aren't consistent across modules: the org-policy key is
 * "Expense" (`Sense.Modules.Organization.Contracts.ApprovalRequestTypes`), but the Workflow request
 * Expense actually submits under is "ExpenseClaim" (`ExpenseClaimCommands.Claim`) - QA-103. Compared
 * case-insensitively so either spelling (and a same-shaped "…Request" suffix some modules use, e.g.
 * Attendance's `AnomalyResolutionFeatures`) resolves to the same key.
 */
const TYPE_KEY_ALIASES: Record<string, ApprovalTypeKey> = {
  leave: "leave",
  leaverequest: "leave",
  overtime: "overtime",
  overtimerequest: "overtime",
  expense: "expense",
  expenseclaim: "expense",
  advance: "advance",
  advancerequest: "advance",
};

/** `null` for a request type with no filter/summary mapping (Requisition, AssetAssignment, ...) -
 *  such a task only ever counts/shows under "Tümü" (see `approvalFilters.ts`). */
export function approvalTypeKey(requestType: string): ApprovalTypeKey | null {
  return TYPE_KEY_ALIASES[requestType.toLowerCase()] ?? null;
}

const TYPE_KEY_LABELS: Record<ApprovalTypeKey, string> = {
  leave: "İzin",
  overtime: "Fazla mesai",
  expense: "Masraf",
  advance: "Avans",
};

/** `RequestType` values with a filter/summary key above use the same label there (spec §5's "Masraf"/
 *  "Avans"/...); a couple of other known types (unit-assignable but not filterable yet) keep their own
 *  label; anything else falls back to "Talep" - never the raw server key (QA-103). */
const OTHER_REQUEST_TYPE_LABELS: Record<string, string> = {
  Requisition: "Kadro talebi",
  AssetAssignment: "Zimmet",
};

export function requestTypeLabel(requestType: string): string {
  const key = approvalTypeKey(requestType);
  if (key) return TYPE_KEY_LABELS[key];
  return OTHER_REQUEST_TYPE_LABELS[requestType] ?? "Talep";
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
