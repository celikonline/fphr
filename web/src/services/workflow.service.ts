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

export async function createPolicy(payload: CreateOrUpdatePolicyPayload): Promise<{ id: string }> {
  const { data } = await apiClient.post<{ id: string }>(POLICIES, payload);
  return data;
}

export async function updatePolicy(id: string, payload: CreateOrUpdatePolicyPayload): Promise<void> {
  await apiClient.put(`${POLICIES}/${id}`, payload);
}

export async function deactivatePolicy(id: string): Promise<void> {
  await apiClient.delete(`${POLICIES}/${id}`);
}

// ---------------------------------------------------------------- Inbox

export interface InboxTaskDto {
  taskId: string;
  approvalRequestId: string;
  requestType: string;
  subjectId: string;
  requesterName?: string;
  stepNumber: number;
  isFallbackRole: boolean;
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

export interface ApprovalStepStatusDto {
  stepNumber: number;
  approverEmployeeId?: string;
  fallbackRoleCode?: string;
  isRequired: boolean;
  status: string;
}

export interface ApprovalStatusDto {
  approvalRequestId: string;
  requestType: string;
  subjectId: string;
  status: string;
  currentStepNumber?: number;
  showCurrentApproverToRequester: boolean;
  steps: ApprovalStepStatusDto[];
}

export async function getApprovalStatus(id: string): Promise<ApprovalStatusDto> {
  const { data } = await apiClient.get<ApprovalStatusDto>(`${REQUESTS}/${id}/status`);
  return data;
}

export interface MyApprovalRequestDto {
  id: string;
  requestType: string;
  status: string;
  submittedAt: string;
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
