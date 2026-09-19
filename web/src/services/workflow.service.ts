/**
 * Workflow service: approval policies, the approver inbox, approval status/history and the
 * public decide-by-token flow. The Workflow module's REST API is being built in parallel;
 * shapes mirror the backend DTOs (ApprovalPolicyDto, InboxTaskDto, ApprovalStatusDto, ...).
 */
import { apiClient } from "@senseik/api-client";

const WORKFLOW_BASE = "/v1/workflow";
const POLICIES = `${WORKFLOW_BASE}/policies`;
const INBOX = `${WORKFLOW_BASE}/inbox`;
const REQUESTS = `${WORKFLOW_BASE}/requests`;
const MY_REQUESTS = `${WORKFLOW_BASE}/my-requests`;
const DECISIONS = `${WORKFLOW_BASE}/decisions`;

// ---------------------------------------------------------------- Approval policies

export interface ApprovalPolicyStepDto {
  stepNumber: number;
  approverEmployeeId: string;
  isRequired: boolean;
}

export interface ApprovalPolicyDto {
  id: string;
  requestType: string;
  name: string;
  isActive: boolean;
  showCurrentApproverToRequester: boolean;
  steps: ApprovalPolicyStepDto[];
}

export interface CreateOrUpdatePolicyPayload {
  requestType: string;
  name: string;
  showCurrentApproverToRequester: boolean;
  steps: { approverEmployeeId: string; isRequired: boolean }[];
}

export async function listPolicies(requestType?: string): Promise<ApprovalPolicyDto[]> {
  const { data } = await apiClient.get<ApprovalPolicyDto[]>(POLICIES, { params: { requestType } });
  return data;
}

export async function getPolicy(id: string): Promise<ApprovalPolicyDto> {
  const { data } = await apiClient.get<ApprovalPolicyDto>(`${POLICIES}/${id}`);
  return data;
}

/** The command returns the new policy's id as a raw GUID (not wrapped in an object). */
export async function createPolicy(payload: CreateOrUpdatePolicyPayload): Promise<string> {
  const { data } = await apiClient.post<string>(POLICIES, payload);
  return data;
}

export async function updatePolicy(id: string, payload: CreateOrUpdatePolicyPayload): Promise<void> {
  await apiClient.put(`${POLICIES}/${id}`, payload);
}

export async function deactivatePolicy(id: string): Promise<void> {
  await apiClient.delete(`${POLICIES}/${id}`);
}

// ---------------------------------------------------------------- Inbox

/** "Overtime" summary field - one of `OvertimeApprovalSummary`/`LeaveApprovalSummary` on `ApprovalRequestSummaryDto`. */
export interface OvertimeApprovalSummaryDto {
  /** DateOnly "yyyy-MM-dd" - see `formatDateOnly`. */
  workDate: string;
  /** TimeOnly "HH:mm:ss" - see `formatTimeOnly`. */
  startTime: string;
  durationMinutes: number;
  reason?: string | null;
}

/** "Leave" summary field - one of `OvertimeApprovalSummary`/`LeaveApprovalSummary` on `ApprovalRequestSummaryDto`. */
export interface LeaveApprovalSummaryDto {
  /** DateOnly "yyyy-MM-dd" - see `formatDateOnly`. */
  startDate: string;
  /** DateOnly "yyyy-MM-dd" - see `formatDateOnly`. */
  endDate: string;
  /** camelCased `LeaveTypeCode` enum name (e.g. "annual", "sick") - translate via `leave:code.${leaveTypeCode}` when leaveTypeName is unset. */
  leaveTypeCode: string;
  leaveTypeName?: string | null;
  days: number;
}

/**
 * Request-type-specific projection of an inbox task's subject entity (OvertimeRequest/LeaveRequest), so the card can
 * show more than a raw subjectId. requestDate is the request's own date (overtime work date / leave start date) -
 * shown next to submittedAt. Exactly one of overtime/leave is set today; both unset means no provider is registered
 * yet for this requestType (Advance/Expense/Requisition).
 */
export interface ApprovalRequestSummaryDto {
  requestDate: string;
  overtime?: OvertimeApprovalSummaryDto | null;
  leave?: LeaveApprovalSummaryDto | null;
}

export interface InboxTaskDto {
  taskId: string;
  approvalRequestId: string;
  requestType: string;
  subjectId: string;
  requesterName?: string;
  stepNumber: number;
  isFallbackRoleTask: boolean;
  /** BE ekliyor (tur 2 sözleşmesi); henüz gelmiyorsa undefined/null olarak ele alınır. */
  submittedAt?: string | null;
  summary?: ApprovalRequestSummaryDto | null;
}

export async function listInbox(): Promise<InboxTaskDto[]> {
  const { data } = await apiClient.get<InboxTaskDto[]>(INBOX);
  return data;
}

export interface DecideTaskPayload {
  outcome: "approved" | "rejected";
  comment?: string;
}

export async function decideTask(taskId: string, payload: DecideTaskPayload): Promise<void> {
  await apiClient.post(`${INBOX}/${taskId}/decide`, payload);
}

export async function overrideDecideTask(taskId: string, payload: DecideTaskPayload): Promise<void> {
  await apiClient.post(`${INBOX}/${taskId}/override-decide`, payload);
}

// ---------------------------------------------------------------- Approval status / history

export interface ApprovalRequestStatusDto {
  approvalRequestId: string;
  requestType: string;
  subjectId: string;
  requesterEmployeeId: string;
  status: string;
  stepCount: number;
  currentStepNumber?: number;
  /** Only populated when the request's ShowCurrentApproverToRequester flag is set and the current task has a concrete assignee. */
  currentApproverEmployeeId?: string;
}

export async function getApprovalStatus(id: string): Promise<ApprovalRequestStatusDto> {
  const { data } = await apiClient.get<ApprovalRequestStatusDto>(`${REQUESTS}/${id}/status`);
  return data;
}

export interface MyApprovalRequestDto {
  approvalRequestId: string;
  requestType: string;
  subjectId: string;
  status: string;
  stepCount: number;
  currentStepNumber?: number;
  submittedAt: string;
  completedAt?: string;
}

export async function listMyRequests(): Promise<MyApprovalRequestDto[]> {
  const { data } = await apiClient.get<MyApprovalRequestDto[]>(MY_REQUESTS);
  return data;
}

// ---------------------------------------------------------------- Decide by token (public, no-auth page)

export interface DecideByTokenPayload {
  token: string;
  outcome: "approved" | "rejected";
  comment?: string;
}

export async function decideByToken(payload: DecideByTokenPayload): Promise<void> {
  await apiClient.post(`${DECISIONS}/by-token`, payload);
}
