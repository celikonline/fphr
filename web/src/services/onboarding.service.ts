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
  categoryId?: string | null;
  importance?: OnboardingImportance;
  requiresEDocument?: boolean;
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
  categoryId?: string | null;
  importance?: OnboardingImportance;
  requiresEDocument?: boolean;
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

/** E5 (B15): task importance, category, named template sets. */
export type OnboardingImportance = "normal" | "high" | "critical";

/** `OnboardingCategoryDto`. */
export interface OnboardingCategory {
  id: string;
  name: string;
  sortOrder: number;
  isActive: boolean;
}

/** `OnboardingCategoryProgressDto`; categoryId is missing for uncategorized tasks (nulls omitted). */
export interface OnboardingCategoryProgress {
  categoryId?: string | null;
  name: string;
  done: number;
  total: number;
  percent: number;
}

/** `OnboardingTemplateSetDto`. */
export interface OnboardingTemplateSet {
  id: string;
  stage: OnboardingStage;
  name: string;
  templateIds: string[];
  isActive: boolean;
}

export async function listOnboardingTemplateSets(): Promise<OnboardingTemplateSet[]> {
  const { data } = await apiClient.get<OnboardingTemplateSet[]>(`${BASE}/template-sets`);
  return data;
}

export async function listOnboardingCategories(): Promise<OnboardingCategory[]> {
  const { data } = await apiClient.get<OnboardingCategory[]>(`${BASE}/categories`);
  return data;
}

export async function getEmployeeCategoryProgress(
  employeeId: string
): Promise<OnboardingCategoryProgress[]> {
  const { data } = await apiClient.get<OnboardingCategoryProgress[]>(
    `${BASE}/employees/${employeeId}/category-progress`
  );
  return data;
}

/** Starts a named template set for the employee (once per set). */
export async function startOnboardingProcess(
  employeeId: string,
  templateSetId: string,
  referenceDate: string
): Promise<void> {
  await apiClient.post(`${BASE}/employees/${employeeId}/start-process`, {
    templateSetId,
    referenceDate,
  });
}

export const ONBOARDING_IMPORTANCES: OnboardingImportance[] = ["normal", "high", "critical"];

export interface SaveCategoryInput {
  name: string;
  sortOrder: number;
  isActive?: boolean;
}

export async function createOnboardingCategory(
  input: SaveCategoryInput
): Promise<OnboardingCategory> {
  const { data } = await apiClient.post<OnboardingCategory>(`${BASE}/categories`, input);
  return data;
}

export async function updateOnboardingCategory(
  id: string,
  input: SaveCategoryInput
): Promise<OnboardingCategory> {
  const { data } = await apiClient.put<OnboardingCategory>(`${BASE}/categories/${id}`, input);
  return data;
}

/** 409 `onboarding.category_in_use` when a template still references the category. */
export async function deleteOnboardingCategory(id: string): Promise<void> {
  await apiClient.delete(`${BASE}/categories/${id}`);
}

/** `templateIds` order is the set's order (uuid[] as saved). */
export async function createOnboardingTemplateSet(input: {
  stage: OnboardingStage;
  name: string;
  templateIds: string[];
}): Promise<OnboardingTemplateSet> {
  const { data } = await apiClient.post<OnboardingTemplateSet>(`${BASE}/template-sets`, input);
  return data;
}

export async function updateOnboardingTemplateSet(
  id: string,
  input: { name: string; templateIds: string[]; isActive: boolean }
): Promise<OnboardingTemplateSet> {
  const { data } = await apiClient.put<OnboardingTemplateSet>(`${BASE}/template-sets/${id}`, input);
  return data;
}
