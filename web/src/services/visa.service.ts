/**
 * Visa document requests (senseik-001, roadmap Faz 1b #31): mirrors `VisaDocumentRequestsController` /
 * `VisaDocumentProcessesController` / `VisaSettingsController` (`api/v1/visa-requests|visa-processes|visa-settings`).
 * An employee submits a request, the tenant's configured approver decides it through the Workflow inbox
 * (request type `VisaDocument`), and on approval the template documents land in the employee's folder.
 */
import { apiClient } from "@senseik/api-client";
import type { PagedResult } from "@/types";

const VISA_BASE = "/v1";

/** Native C# enum, camelCase JsonStringEnumConverter. */
export type VisaRequestStatus = "pending" | "awaitingApproval" | "approved" | "rejected" | "completed";
export const VISA_REQUEST_STATUSES: VisaRequestStatus[] = ["pending", "awaitingApproval", "approved", "rejected", "completed"];

/** Seed-defined lookup (`VisaTypes` in the Documents domain); stored on the request as this lowercase code. */
export type VisaType = "tourist" | "business" | "work" | "student" | "transit";
export const VISA_TYPES: VisaType[] = ["tourist", "business", "work", "student", "transit"];

export interface VisaRequest {
  id: string;
  employeeId: string;
  visaDocumentProcessId?: string | null;
  /** ISO timestamps at UTC midnight - only the date part is meaningful. */
  travelStart: string;
  travelEnd: string;
  countryCode: string;
  visaType: string;
  description: string;
  status: VisaRequestStatus;
  approvalRequestId?: string | null;
  /** Absent when the approver gave no reason (Workflow's rejection event carries none). */
  rejectionReason?: string | null;
  createdAt: string;
  approvedAt?: string | null;
  completedAt?: string | null;
}

export interface SubmitVisaRequestInput {
  visaDocumentProcessId: string | null;
  /** "yyyy-MM-dd" */
  travelStart: string;
  travelEnd: string;
  countryCode: string;
  visaType: VisaType;
  description: string | null;
}

export interface SubmitVisaRequestResult {
  requestId: string;
  status: VisaRequestStatus;
  approvalRequestId: string;
}

export interface VisaProcess {
  id: string;
  name: string;
  countryCode?: string | null;
  isActive: boolean;
}

export interface VisaProcessInput {
  name: string;
  countryCode: string | null;
}

export interface VisaDeliveredDocument {
  id: string;
  title: string;
  contentType: string;
  size: number;
  uploadedAt: string;
  /** Presigned, short-lived (15 min) - fetch the list again rather than caching it. */
  downloadUrl: string;
  downloadUrlExpiresAt: string;
}

export interface VisaSettings {
  approverEmployeeId?: string | null;
  templateDocumentIds: string[];
}

export interface VisaTemplateDocumentCandidate {
  id: string;
  title: string;
  contentType: string;
  size: number;
  uploadedAt: string;
  status: string;
}

/** The caller's own requests, newest first. */
export async function listMyVisaRequests(status?: VisaRequestStatus): Promise<VisaRequest[]> {
  const { data } = await apiClient.get<VisaRequest[]>(`${VISA_BASE}/visa-requests`, { params: status ? { status } : undefined });
  return data;
}

export async function submitVisaRequest(input: SubmitVisaRequestInput): Promise<SubmitVisaRequestResult> {
  const { data } = await apiClient.post<SubmitVisaRequestResult>(`${VISA_BASE}/visa-requests`, input);
  return data;
}

/** Documents delivered into the caller's folder after approval (empty until then). */
export async function listVisaRequestDocuments(requestId: string): Promise<VisaDeliveredDocument[]> {
  const { data } = await apiClient.get<VisaDeliveredDocument[]>(`${VISA_BASE}/visa-requests/${requestId}/documents`);
  return data;
}

/** Active processes only - the request form's selection list. */
export async function listVisaProcesses(): Promise<VisaProcess[]> {
  const { data } = await apiClient.get<VisaProcess[]>(`${VISA_BASE}/visa-processes`);
  return data;
}

/** Admin definition list (`documents.visa.manage`): every process including inactive ones. */
export async function listAllVisaProcesses(): Promise<VisaProcess[]> {
  const { data } = await apiClient.get<VisaProcess[]>(`${VISA_BASE}/visa-processes/manage`);
  return data;
}

export async function createVisaProcess(input: VisaProcessInput): Promise<VisaProcess> {
  const { data } = await apiClient.post<VisaProcess>(`${VISA_BASE}/visa-processes/manage`, input);
  return data;
}

export async function updateVisaProcess(id: string, input: VisaProcessInput): Promise<VisaProcess> {
  const { data } = await apiClient.put<VisaProcess>(`${VISA_BASE}/visa-processes/manage/${id}`, input);
  return data;
}

export async function setVisaProcessActive(id: string, isActive: boolean): Promise<VisaProcess> {
  const { data } = await apiClient.put<VisaProcess>(`${VISA_BASE}/visa-processes/manage/${id}/active`, { isActive });
  return data;
}

export async function getVisaSettings(): Promise<VisaSettings> {
  const { data } = await apiClient.get<VisaSettings>(`${VISA_BASE}/visa-settings`);
  return data;
}

export async function updateVisaSettings(input: { approverEmployeeId: string | null; templateDocumentIds: string[] }): Promise<VisaSettings> {
  const { data } = await apiClient.put<VisaSettings>(`${VISA_BASE}/visa-settings`, input);
  return data;
}

/** Tenant documents that can be picked as templates (`documents.files.read`; delivered copies are excluded server-side). */
export async function listTemplateCandidates(pageSize = 100): Promise<VisaTemplateDocumentCandidate[]> {
  const { data } = await apiClient.get<PagedResult<VisaTemplateDocumentCandidate>>(`${VISA_BASE}/documents`, { params: { page: 1, pageSize } });
  return data.items;
}
