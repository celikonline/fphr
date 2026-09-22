/**
 * İşe alış / işten çıkış görev listeleri: mirrors `OnboardingController` (`api/v1/onboarding`). Checklists are created
 * automatically on EmployeeHired / EmployeeTerminated; HR can also create one manually for older employees.
 */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/onboarding";

/** Native C# enums, camelCase JsonStringEnumConverter. */
export type OnboardingStage = "onboarding" | "offboarding";
export const ONBOARDING_STAGES: OnboardingStage[] = ["onboarding", "offboarding"];
export type OnboardingTaskStatus = "pending" | "completed" | "waived";

/** Responsible parties the UI offers; stored as free-text role codes server-side. */
export const RESPONSIBLE_ROLES = ["hr", "it", "manager", "payroll"] as const;

/** `OnboardingTaskTemplateDto`. */
export interface OnboardingTaskTemplate {
  id: string;
  stage: OnboardingStage;
  title: string;
  responsibleRoleCode: string;
  dueDateOffsetDays: number;
  isMandatory: boolean;
  isActive: boolean;
}

/** `OnboardingChecklistItemDto`; DateOnly fields are "yyyy-MM-dd". */
export interface OnboardingChecklistItem {
  id: string;
  employeeId: string;
  templateId: string;
  title: string;
  responsibleRoleCode: string;
  isMandatory: boolean;
  stage: OnboardingStage;
  dueDate: string;
  status: OnboardingTaskStatus;
  isOverdue: boolean;
  completedAt?: string | null;
  waivedReason?: string | null;
  waivedAt?: string | null;
}

export interface SaveTemplateInput {
  stage: OnboardingStage;
  title: string;
  responsibleRoleCode: string;
  dueDateOffsetDays: number;
  isMandatory: boolean;
}

export async function listOnboardingTemplates(): Promise<OnboardingTaskTemplate[]> {
  const { data } = await apiClient.get<OnboardingTaskTemplate[]>(`${BASE}/templates`);
  return data;
}

export async function createOnboardingTemplate(
  input: SaveTemplateInput
): Promise<OnboardingTaskTemplate> {
  const { data } = await apiClient.post<OnboardingTaskTemplate>(`${BASE}/templates`, input);
  return data;
}

export async function updateOnboardingTemplate(
  id: string,
  input: SaveTemplateInput
): Promise<OnboardingTaskTemplate> {
  const { data } = await apiClient.put<OnboardingTaskTemplate>(`${BASE}/templates/${id}`, input);
  return data;
}

export async function setOnboardingTemplateActive(id: string, isActive: boolean): Promise<void> {
  await apiClient.post(`${BASE}/templates/${id}/${isActive ? "activate" : "deactivate"}`);
}

/** Loads the recommended templates when the tenant has none; returns how many were added. */
export async function seedDefaultOnboardingTemplates(): Promise<number> {
  const { data } = await apiClient.post<number>(`${BASE}/templates/seed-defaults`);
  return data;
}

export async function listPendingOnboardingTasks(): Promise<OnboardingChecklistItem[]> {
  const { data } = await apiClient.get<OnboardingChecklistItem[]>(`${BASE}/tasks/pending`);
  return data;
}

export async function listEmployeeOnboardingTasks(
  employeeId: string
): Promise<OnboardingChecklistItem[]> {
  const { data } = await apiClient.get<OnboardingChecklistItem[]>(
    `${BASE}/employees/${employeeId}/tasks`
  );
  return data;
}

export async function createEmployeeChecklist(
  employeeId: string,
  stage: OnboardingStage,
  referenceDate: string
): Promise<void> {
  await apiClient.post(`${BASE}/employees/${employeeId}/tasks`, { stage, referenceDate });
}

export async function completeOnboardingTask(id: string): Promise<void> {
  await apiClient.post(`${BASE}/tasks/${id}/complete`);
}

export async function waiveOnboardingTask(id: string, reason: string): Promise<void> {
  await apiClient.post(`${BASE}/tasks/${id}/waive`, { reason });
}
