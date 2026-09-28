import { leaveService, type LeaveRequestAttachment } from "@/services/LeaveService";
import type { InboxTask } from "@/services/ApprovalsService";
import i18n from "@/lib/i18n";

/**
 * A1: fetches a Leave task's attachments so the approver knows a document was attached before
 * deciding. Never throws - missing `requesterEmployeeId` (older API build), 403 (no Manager scope
 * over this employee), 404 (request no longer exists) and any network error all just hide the row
 * instead of showing an error; the axios error object is never logged. Non-Leave tasks (Overtime,
 * Advance, ...) skip the request entirely - this is the only inbox detail endpoint mobile calls,
 * and only for the request type that actually has attachments.
 */
export async function loadLeaveTaskAttachments(
  task: Pick<InboxTask, "requestType" | "requesterEmployeeId" | "subjectId">
): Promise<LeaveRequestAttachment[]> {
  if (task.requestType !== "Leave" || !task.requesterEmployeeId) {
    return [];
  }
  try {
    const detail = await leaveService.getEmployeeLeaveRequest(
      task.requesterEmployeeId,
      task.subjectId
    );
    return detail.attachments ?? [];
  } catch {
    return [];
  }
}

/** A1: "📎 N belge: ad, ad" - `null` when there is nothing to show (no row rendered). Kept for callers that just need a plain summary string; the card itself now renders each name as its own tappable row (senseik-mobile-011) instead of this joined form. */
export function formatAttachmentsSummary(
  attachments: Pick<LeaveRequestAttachment, "fileName">[]
): string | null {
  if (attachments.length === 0) {
    return null;
  }
  return `${attachmentsCountLabel(attachments.length)} ${attachments.map((a) => a.fileName).join(", ")}`;
}

/** senseik-mobile-011: "📎 N belge:" header shown above the tappable attachment rows. */
export function attachmentsCountLabel(count: number): string {
  return `📎 ${i18n.t("approvals:attachmentsCount", { count })}`;
}
