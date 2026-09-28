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
  /**
   * A1: the request's owner, populated for every task (including fallback-role ones) -
   * `ApprovalRequestDtos.cs:24-33`. Used only to fetch a Leave task's attachments
   * (`GET /leave/employees/{requesterEmployeeId}/requests/{subjectId}`); optional here too in case
   * an older API build hasn't rolled out yet - absence just skips the attachment lookup.
   */
  requesterEmployeeId?: string | null;
}

export type DecisionOutcome = "approved" | "rejected";

/** `DecideTaskPayload` (web `workflow.service.ts`) body, same field names. */
export interface DecideTaskInput {
  outcome: DecisionOutcome;
  comment?: string;
}

/** `ApprovalRequestStatus` (Workflow.Domain) - the parent request's current state, not this task's own outcome. */
export type ApprovalRequestStatus = "pending" | "approved" | "rejected" | "cancelled";

/**
 * FG-008: one task the caller decided (`ApprovalHistoryItemDto`, `GET inbox/history`), newest
 * decision first. `outcome` is the caller's own decision on this task; `requestStatus` is the
 * parent request's current state (a step-1 approval can still end in a later step's rejection).
 */
export interface ApprovalHistoryItem {
  taskId: string;
  approvalRequestId: string;
  requestType: string;
  subjectId: string;
  requesterEmployeeId: string;
  requesterName?: string | null;
  stepNumber: number;
  outcome: DecisionOutcome;
  decidedAt: string;
  comment?: string | null;
  requestStatus: ApprovalRequestStatus;
  summary?: InboxTaskSummary | null;
}

/** `PagedResult<ApprovalHistoryItemDto>` (Sense.Shared.Contracts.Paging) - only the fields this app reads. */
export interface ApprovalHistoryPage {
  items: ApprovalHistoryItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface GetHistoryParams {
  from?: string;
  to?: string;
  type?: string;
  page?: number;
  pageSize?: number;
}

/** `DecideBulkRequest` (Workflow.Api) body. Up to `WorkflowLimits.BulkDecisionMaxTasks` (50) task ids. */
export interface DecideBulkInput {
  taskIds: string[];
  outcome: DecisionOutcome;
  comment?: string;
}

/**
 * `DecideBulkItemResponse` - one task's decide-bulk outcome. `errorMessage` is already localized
 * server-side (Accept-Language) - show it as-is, no client-side mapping needed.
 */
export interface BulkDecisionItemResult {
  taskId: string;
  succeeded: boolean;
  errorCode?: string | null;
  errorMessage?: string | null;
}

export class ApprovalsService {
  async getInbox(): Promise<InboxTask[]> {
    const response = await apiClient.get<InboxTask[]>(`${WORKFLOW_BASE}/inbox`);
    return response.data;
  }

  async decideTask(taskId: string, input: DecideTaskInput): Promise<void> {
    await apiClient.post(`${WORKFLOW_BASE}/inbox/${taskId}/decide`, input);
  }

  /** FG-008: the caller's own past decisions, newest first (`GET inbox/history`). */
  async getHistory(params: GetHistoryParams = {}): Promise<ApprovalHistoryPage> {
    const response = await apiClient.get<ApprovalHistoryPage>(`${WORKFLOW_BASE}/inbox/history`, {
      params,
    });
    return { ...response.data, items: response.data.items ?? [] };
  }

  /** FG-008: decides several pending tasks with one outcome (`POST inbox/decide-bulk`) - one result per task, in request order. */
  async decideBulk(input: DecideBulkInput): Promise<BulkDecisionItemResult[]> {
    const response = await apiClient.post<{ results: BulkDecisionItemResult[] }>(
      `${WORKFLOW_BASE}/inbox/decide-bulk`,
      input
    );
    return response.data.results;
  }
}

export const approvalsService = new ApprovalsService();
