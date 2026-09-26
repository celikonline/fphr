import { apiClient } from "@/lib/apiClient";

/**
 * Approvals module (self-service "Onaylarım"): mirrors `InboxController`'s `/workflow/inbox` group
 * (`GET inbox`, `POST inbox/{taskId}/decide`) - the same endpoints `web/src/services/workflow.service.ts`
 * calls. Only the caller's own approval inbox: list pending tasks + approve/reject a task. Override-decide
 * (`workflow.request.override`, HR/admin-only) and approval-policy management stay web-only for now.
 * `InboxTaskDto` now carries a `summary` (overtime/leave request content) alongside requester, request
 * type and step number, so the approver can see what they're deciding without leaving the inbox.
 */
const WORKFLOW_BASE = "/api/v1/workflow";

/** `InboxTaskSummary.overtime`: set when `requestType === "Overtime"`. */
export interface InboxTaskOvertimeSummary {
  workDate: string;
  startTime: string;
  durationMinutes: number;
  reason?: string | null;
}

/** `InboxTaskSummary.leave`: set when `requestType === "Leave"`. */
export interface InboxTaskLeaveSummary {
  startDate: string;
  endDate: string;
  leaveTypeCode: string;
  leaveTypeName?: string | null;
  days: number;
  description?: string | null;
}

/** `InboxTaskSummaryDto`: what the requester is asking for, resolved per `requestType`. Absent on older API builds. */
export interface InboxTaskSummary {
  requestDate: string;
  overtime?: InboxTaskOvertimeSummary | null;
  leave?: InboxTaskLeaveSummary | null;
}

/** `InboxTaskDto`: one pending task in the caller's inbox (assigned directly, or via a fallback role queue). */
export interface InboxTask {
  taskId: string;
  approvalRequestId: string;
  requestType: string;
  subjectId: string;
  requesterName?: string | null;
  stepNumber: number;
  isFallbackRoleTask: boolean;
  /** BE ekliyor (faz-10 tur 2 sözleşmesi); eski yanıtlarda gelmeyebilir. */
  submittedAt?: string | null;
  /** Absent on older API builds (falls back to the request type + requester name only). */
  summary?: InboxTaskSummary | null;
}

export type DecisionOutcome = "approved" | "rejected";

/** `DecideTaskPayload` (web `workflow.service.ts`) body, same field names. */
export interface DecideTaskInput {
  outcome: DecisionOutcome;
  comment?: string;
}

export class ApprovalsService {
  async getInbox(): Promise<InboxTask[]> {
    const response = await apiClient.get<InboxTask[]>(`${WORKFLOW_BASE}/inbox`);
    return response.data;
  }

  async decideTask(taskId: string, input: DecideTaskInput): Promise<void> {
    await apiClient.post(`${WORKFLOW_BASE}/inbox/${taskId}/decide`, input);
  }
}

export const approvalsService = new ApprovalsService();
