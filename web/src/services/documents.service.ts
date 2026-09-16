/**
 * Documents module (Faz 4 D1/D2): mirrors `DocumentTypesController` / `EmployeeDocumentRequirementsController`
 * (`api/v1/documents/...`). Document type catalog + per-employee requirement tracking; the underlying `Document`
 * upload flow (Item 15) is a separate, pre-existing surface (see company.service.ts for its known contract gap).
 */
import { apiClient } from "@senseik/api-client";
import type { PagedQueryParams, PagedResult } from "@/types";

const DOCUMENTS_BASE = "/v1/documents";

/** Native C# enum, camelCase JsonStringEnumConverter. */
export type DocumentTypeStage = "onboarding" | "conditional" | "duringEmployment" | "offboarding" | "recommended";
export const DOCUMENT_TYPE_STAGES: DocumentTypeStage[] = ["onboarding", "conditional", "duringEmployment", "offboarding", "recommended"];

export type DocumentRequirementCondition =
  | "ageUnder18"
  | "hasDisability"
  | "nonTurkishNational"
  | "hasIncentive"
  | "usesDisabilityTaxReduction"
  | "exConvictOrTerrorVictim"
  | "requiresPeriodicHealthCheck"
  | "requiresDriving"
  | "hasOpenAssets"
  | "veryHazardousJob"
  | "hasOvertimeConsent"
  | "isShiftWorker";

export const DOCUMENT_REQUIREMENT_CONDITIONS: DocumentRequirementCondition[] = [
  "ageUnder18",
  "hasDisability",
  "nonTurkishNational",
  "hasIncentive",
  "usesDisabilityTaxReduction",
  "exConvictOrTerrorVictim",
  "requiresPeriodicHealthCheck",
  "requiresDriving",
  "hasOpenAssets",
  "veryHazardousJob",
  "hasOvertimeConsent",
  "isShiftWorker",
];

export type DocumentRequirementStatus = "missing" | "uploaded" | "waived";

/** A tenant's (editable copy of the seeded, or tenant-custom) personnel document type. */
export interface DocumentType {
  id: string;
  code: string;
  name: string;
  stage: DocumentTypeStage;
  isRequired: boolean;
  condition: DocumentRequirementCondition | null;
  renewalPeriodDays: number | null;
  quantity: number;
  isSensitive: boolean;
  defaultRetentionDays: number | null;
  isSystem: boolean;
  isActive: boolean;
  sortOrder: number;
}

export interface CreateDocumentTypeInput {
  code: string;
  name: string;
  stage: DocumentTypeStage;
  isRequired: boolean;
  condition: DocumentRequirementCondition | null;
  renewalPeriodDays: number | null;
  quantity: number;
  isSensitive: boolean;
  defaultRetentionDays: number | null;
  sortOrder: number;
}

/** Editable fields only (not code/stage/condition/isSystem). */
export interface UpdateDocumentTypeInput {
  name: string;
  isRequired: boolean;
  renewalPeriodDays: number | null;
  quantity: number;
  isSensitive: boolean;
  defaultRetentionDays: number | null;
  sortOrder: number;
}

export interface EmployeeDocumentRequirement {
  id: string;
  employeeId: string;
  documentTypeId: string;
  documentTypeCode: string;
  documentTypeName: string;
  stage: DocumentTypeStage;
  status: DocumentRequirementStatus;
  dueDate: string | null;
  documentId: string | null;
  waivedReason: string | null;
}

export interface MissingDocumentReportItem {
  requirementId: string;
  employeeId: string;
  employeeName: string;
  documentTypeCode: string;
  documentTypeName: string;
  stage: DocumentTypeStage;
  status: DocumentRequirementStatus;
  dueDate: string | null;
}

export async function listDocumentTypes(params: { stage?: DocumentTypeStage; isActive?: boolean } = {}): Promise<DocumentType[]> {
  const { data } = await apiClient.get<DocumentType[]>(`${DOCUMENTS_BASE}/types`, { params });
  return data;
}

export async function createDocumentType(input: CreateDocumentTypeInput): Promise<DocumentType> {
  const { data } = await apiClient.post<DocumentType>(`${DOCUMENTS_BASE}/types`, input);
  return data;
}

export async function updateDocumentType(id: string, input: UpdateDocumentTypeInput): Promise<DocumentType> {
  const { data } = await apiClient.put<DocumentType>(`${DOCUMENTS_BASE}/types/${id}`, input);
  return data;
}

export async function setDocumentTypeActive(id: string, active: boolean): Promise<DocumentType> {
  const { data } = await apiClient.post<DocumentType>(`${DOCUMENTS_BASE}/types/${id}/${active ? "activate" : "deactivate"}`);
  return data;
}

export async function getEmployeeDocumentRequirements(employeeId: string): Promise<EmployeeDocumentRequirement[]> {
  const { data } = await apiClient.get<EmployeeDocumentRequirement[]>(`${DOCUMENTS_BASE}/employees/${employeeId}/requirements`);
  return data;
}

export async function getMyDocumentRequirements(): Promise<EmployeeDocumentRequirement[]> {
  const { data } = await apiClient.get<EmployeeDocumentRequirement[]>(`${DOCUMENTS_BASE}/me/requirements`);
  return data;
}

export async function getMissingDocumentReport(
  params: PagedQueryParams & { status?: DocumentRequirementStatus } = {},
): Promise<PagedResult<MissingDocumentReportItem>> {
  const { data } = await apiClient.get<PagedResult<MissingDocumentReportItem>>(`${DOCUMENTS_BASE}/requirements/report`, { params });
  return data;
}

export async function waiveDocumentRequirement(requirementId: string, reason: string): Promise<EmployeeDocumentRequirement> {
  const { data } = await apiClient.post<EmployeeDocumentRequirement>(`${DOCUMENTS_BASE}/requirements/${requirementId}/waive`, { reason });
  return data;
}

export async function clearWaiveDocumentRequirement(requirementId: string): Promise<EmployeeDocumentRequirement> {
  const { data } = await apiClient.post<EmployeeDocumentRequirement>(`${DOCUMENTS_BASE}/requirements/${requirementId}/clear-waive`);
  return data;
}

export async function reevaluateEmployeeDocumentRequirements(employeeId: string): Promise<EmployeeDocumentRequirement[]> {
  const { data } = await apiClient.post<EmployeeDocumentRequirement[]>(`${DOCUMENTS_BASE}/employees/${employeeId}/requirements/reevaluate`);
  return data;
}
