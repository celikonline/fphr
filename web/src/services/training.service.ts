/**
 * Eğitim modülü: mirrors `TrainingProgramsController`, `EnrollmentsController` and `TrainingRequirementsController`
 * (`api/v1/training/...`). DTO enum fields carry C# `Enum.ToString()` (PascalCase, e.g. "SoftSkills"); requests use the
 * camelCase enum converter, so {@link enumKey} maps a DTO value onto the request/i18n form.
 */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/training";

export const TRAINING_CATEGORIES = ["technical", "compliance", "softSkills", "custom"] as const;
export type TrainingCategory = (typeof TRAINING_CATEGORIES)[number];
export type TrainingStatus = "draft" | "active" | "closed" | "archived";
export type EnrollmentStatus = "enrolled" | "completed" | "cancelled" | "noShow";

/** "SoftSkills" → "softSkills". */
export function enumKey<T extends string>(value: string): T {
  return (value.charAt(0).toLowerCase() + value.slice(1)) as T;
}

/** `TrainingProgramDto`. */
export interface TrainingProgram {
  id: string;
  title: string;
  description?: string | null;
  category: string;
  startDate: string;
  endDate?: string | null;
  maxCapacity?: number | null;
  status: string;
}

/** `EnrollmentDto`. */
export interface Enrollment {
  id: string;
  trainingProgramId: string;
  employeeId: string;
  status: string;
  enrolledAt: string;
  completedAt?: string | null;
  certificateNumber?: string | null;
}

/** `MyTrainingDto`. */
export interface MyTraining {
  enrollmentId: string;
  trainingProgramId: string;
  title: string;
  description?: string | null;
  category: string;
  startDate: string;
  endDate?: string | null;
  programStatus: string;
  status: string;
  enrolledAt: string;
  completedAt?: string | null;
  certificateNumber?: string | null;
}

/** `TrainingRequirementDto`. */
export interface TrainingRequirement {
  id: string;
  trainingProgramId: string;
  prerequisiteTrainingProgramId: string;
  blocksAssignmentUntilCompleted: boolean;
}

export interface CreateTrainingProgramInput {
  title: string;
  description: string | null;
  category: TrainingCategory;
  /** ISO instants. */
  startDate: string;
  endDate: string;
  maxCapacity: number | null;
}

export async function listTrainingPrograms(): Promise<TrainingProgram[]> {
  const { data } = await apiClient.get<TrainingProgram[]>(`${BASE}/programs`);
  return data;
}

export async function createTrainingProgram(input: CreateTrainingProgramInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${BASE}/programs`, input);
  return data;
}

export async function activateTrainingProgram(id: string): Promise<void> {
  await apiClient.post(`${BASE}/programs/${id}/activate`);
}

export async function closeTrainingProgram(id: string): Promise<void> {
  await apiClient.post(`${BASE}/programs/${id}/close`);
}

export async function listEnrollments(trainingProgramId: string): Promise<Enrollment[]> {
  const { data } = await apiClient.get<Enrollment[]>(`${BASE}/enrollments`, { params: { trainingProgramId } });
  return data;
}

export async function getMyTrainings(): Promise<MyTraining[]> {
  const { data } = await apiClient.get<MyTraining[]>(`${BASE}/enrollments/my`);
  return data;
}

export async function enrollEmployee(trainingProgramId: string, employeeId: string): Promise<string> {
  const { data } = await apiClient.post<string>(`${BASE}/enrollments`, { trainingProgramId, employeeId });
  return data;
}

export async function completeEnrollment(id: string, certificateNumber: string | null): Promise<void> {
  await apiClient.post(`${BASE}/enrollments/${id}/complete`, { certificateNumber });
}

/** Removes the enrollment (the API deletes it rather than keeping a cancelled row). */
export async function cancelEnrollment(id: string): Promise<void> {
  await apiClient.post(`${BASE}/enrollments/${id}/cancel`);
}

export async function listTrainingRequirements(trainingProgramId: string): Promise<TrainingRequirement[]> {
  const { data } = await apiClient.get<TrainingRequirement[]>(`${BASE}/requirements`, { params: { trainingProgramId } });
  return data;
}

export async function createTrainingRequirement(
  trainingProgramId: string,
  prerequisiteTrainingProgramId: string,
  blocksAssignmentUntilCompleted: boolean
): Promise<string> {
  const { data } = await apiClient.post<string>(`${BASE}/requirements`, {
    trainingProgramId,
    prerequisiteTrainingProgramId,
    blocksAssignmentUntilCompleted,
  });
  return data;
}

export async function deleteTrainingRequirement(id: string): Promise<void> {
  await apiClient.delete(`${BASE}/requirements/${id}`);
}
