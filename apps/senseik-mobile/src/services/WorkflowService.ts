import { apiClient } from "@/lib/apiClient";

/**
 * FG-009: request detail - the approval chain's step timeline, comments and the caller's own
 * submitted requests (for the subjectId -> approvalRequestId lookup Taleplerim needs). Mirrors
 * `RequestsController`/`MyRequestsController` (`Sense.Modules.Workflow.Api`). No flat permission on
 * status/comments - the handler authorizes the caller as a party of the request (requester, any
 * task's approver/decider, fallback role member, or `workflow.request.override` holder); anyone
 * else gets 404 `workflow.request.not_found`, so a request's existence is never leaked.
 */
const WORKFLOW_BASE = "/api/v1/workflow";

/** `WorkflowLimits.CommentMaxLength` (`Sense.Modules.Workflow.Domain`) - kept in sync manually. */
export const COMMENT_MAX_LENGTH = 1000;

/** `ApprovalStepStatusDto` (`ApprovalRequestDtos.cs`). `status` is camelCase ("pending"/"approved"/
 * "rejected") unlike the parent request's own `status` field - compare both case-insensitively
 * regardless, in case a future API build changes casing. */
export interface ApprovalStepStatus {
  stepNumber: number;
  status: string;
  approverEmployeeId?: string | null;
  approverName?: string | null;
  isFallbackRole: boolean;
  decidedAt?: string | null;
  comment?: string | null;
}

/** `ApprovalRequestStatusDto`. `status` is PascalCase ("Pending"/"Approved"/"Rejected"/"Cancelled") -
 * the backend produces it with `ToString()` on the domain enum; compare case-insensitively. */
export interface ApprovalRequestStatusDetail {
  approvalRequestId: string;
  requestType: string;
  subjectId: string;
  requesterEmployeeId: string;
  status: string;
  stepCount: number;
  currentStepNumber?: number | null;
  currentApproverEmployeeId?: string | null;
  submittedAt: string;
  completedAt?: string | null;
  steps: ApprovalStepStatus[];
}

/** `ApprovalRequestCommentDto` - oldest first. */
export interface RequestComment {
  id: string;
  authorEmployeeId: string;
  authorName?: string | null;
  text: string;
  createdAt: string;
}

/** `MyApprovalRequestDto` (`GET workflow/my-requests`) - one row per request the caller submitted;
 * used only to map a Taleplerim card's own id (`subjectId`) to its `approvalRequestId`. */
export interface MyApprovalRequestSummary {
  approvalRequestId: string;
  requestType: string;
  subjectId: string;
  status: string;
  stepCount: number;
  currentStepNumber?: number | null;
  submittedAt: string;
  completedAt?: string | null;
}

export class WorkflowService {
  /** The caller's own submitted requests, for the Taleplerim card -> approvalRequestId lookup. */
  async getMyRequests(): Promise<MyApprovalRequestSummary[]> {
    const response = await apiClient.get<MyApprovalRequestSummary[]>(
      `${WORKFLOW_BASE}/my-requests`
    );
    return response.data ?? [];
  }

  async getStatus(approvalRequestId: string): Promise<ApprovalRequestStatusDetail> {
    const response = await apiClient.get<ApprovalRequestStatusDetail>(
      `${WORKFLOW_BASE}/requests/${approvalRequestId}/status`
    );
    return response.data;
  }

  /** Oldest first (`[]` when there are none). */
  async getComments(approvalRequestId: string): Promise<RequestComment[]> {
    const response = await apiClient.get<RequestComment[]>(
      `${WORKFLOW_BASE}/requests/${approvalRequestId}/comments`
    );
    return response.data ?? [];
  }

  /** `text` must be 1..`COMMENT_MAX_LENGTH` characters (server-validated regardless). */
  async addComment(approvalRequestId: string, text: string): Promise<{ id: string }> {
    const response = await apiClient.post<{ id: string }>(
      `${WORKFLOW_BASE}/requests/${approvalRequestId}/comments`,
      { text }
    );
    return response.data;
  }
}

export const workflowService = new WorkflowService();
