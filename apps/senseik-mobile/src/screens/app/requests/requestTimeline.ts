import type {
  ApprovalRequestStatusDetail,
  ApprovalStepStatus,
  MyApprovalRequestSummary,
} from "@/services/WorkflowService";
import type { RequestKind } from "@/types/navigation";
import type { Tone } from "@/theme/theme";

export type { RequestDetailSummary } from "@/types/navigation";

/**
 * `RequestKind` -> the backend `requestType` string `MyApprovalRequestDto`/`ApprovalRequestStatusDto`
 * carry. Not the same spelling as `Organization.Contracts.ApprovalRequestTypes`' unit-assignable
 * "Expense" key (QA-103) - Expense's own submitted requests use "ExpenseClaim"
 * (`ExpenseClaimCommands.Claim`), confirmed against `src/Modules/Expense/**` and the FG-009 backend
 * (P06) report's contract table.
 */
export const REQUEST_KIND_TO_REQUEST_TYPE: Record<RequestKind, string> = {
  leave: "Leave",
  overtime: "Overtime",
  expense: "ExpenseClaim",
  advance: "Advance",
  punchCorrection: "PunchCorrection",
};

/**
 * FG-009 kontrolcü notu: a Taleplerim card's own id is the approval request's `subjectId`, not its
 * `approvalRequestId` - the two are only linked through `GET workflow/my-requests`. Matched on both
 * `subjectId` and the kind's `requestType` (not `subjectId` alone), since a `LeaveRequest.Id` and an
 * unrelated `ExpenseClaim.Id` could theoretically collide as GUIDs are type-agnostic. Returns
 * `undefined` when no match is found - the card must not be tappable in that case (no error shown).
 */
export function findApprovalRequestId(
  myRequests: readonly MyApprovalRequestSummary[],
  subjectId: string,
  kind: RequestKind
): string | undefined {
  const requestType = REQUEST_KIND_TO_REQUEST_TYPE[kind];
  return myRequests.find((r) => r.subjectId === subjectId && r.requestType === requestType)
    ?.approvalRequestId;
}

export const COMMENT_MAX_LENGTH = 1000;

/** Trimmed, non-empty, within the server's limit (`WorkflowService.COMMENT_MAX_LENGTH`). */
export function canSubmitComment(text: string): boolean {
  const trimmed = text.trim();
  return trimmed.length > 0 && trimmed.length <= COMMENT_MAX_LENGTH;
}

/** FG-009 backend decision (P06 §"Kararlar"): comments are closed only on a `Cancelled` request -
 * an approved/rejected one can still take a comment (e.g. "why was this rejected?"). Case-insensitive,
 * `status` is the parent request's PascalCase status. */
export function isCommentingAllowed(status: string): boolean {
  return status.toLowerCase() !== "cancelled";
}

/** The one step actually being waited on right now, or `undefined` once the request is no longer
 * pending (nothing is "current" on a finished request). */
export function highlightedStepNumber(
  detail: Pick<ApprovalRequestStatusDetail, "status" | "currentStepNumber">
): number | undefined {
  return detail.status.toLowerCase() === "pending"
    ? (detail.currentStepNumber ?? undefined)
    : undefined;
}

export type StepVisualStatus = "approved" | "rejected" | "pending" | "notReached";

/** A step's visual state for the timeline dot. A step that still reads "pending" after the whole
 * request finished was never reached (e.g. rejected at step 1 - step 2 never ran). */
export function stepVisualStatus(
  step: Pick<ApprovalStepStatus, "status">,
  request: Pick<ApprovalRequestStatusDetail, "status">
): StepVisualStatus {
  const stepStatus = step.status.toLowerCase();
  if (stepStatus === "approved") return "approved";
  if (stepStatus === "rejected") return "rejected";
  return request.status.toLowerCase() === "pending" ? "pending" : "notReached";
}

export const STEP_VISUAL_TONES: Record<StepVisualStatus, Tone> = {
  approved: "success",
  rejected: "danger",
  pending: "warning",
  notReached: "neutral",
};

/** Overall request status ("Pending"/"Approved"/"Rejected"/"Cancelled") -> the `leave:status.*` tone
 * set (same four-state palette Taleplerim/İzinlerim already use for `LeaveRequestStatus`). */
export function overallStatusTone(status: string): Tone {
  const lower = status.toLowerCase();
  if (lower === "approved") return "success";
  if (lower === "rejected") return "danger";
  if (lower === "cancelled") return "neutral";
  return "warning";
}

/** i18n key for the overall status label - reuses `leave:status.*` (already covers
 * pending/approved/rejected/cancelled) instead of a duplicate translation set. */
export function overallStatusLabelKey(status: string): string {
  const lower = status.toLowerCase();
  const known = lower === "approved" || lower === "rejected" || lower === "cancelled";
  return `leave:status.${known ? lower : "pending"}`;
}
