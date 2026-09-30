/** Vardiya rotasyonları (attendance.rotation.view/manage) — B9 RotationsController sözleşmesi. */
import { apiClient } from "@senseik/api-client";

const BASE = "/v1/attendance/rotations";

export type RotationPreset = "custom" | "twoByTwelve" | "threeByEight" | "fourBySix";
export type RotationDayType = "work" | "off";

export interface RotationStep {
  dayOffset: number;
  /** Absent on off days (and on work days the user has not mapped yet). */
  shiftDefinitionId?: string;
  dayType: RotationDayType;
}

export interface Rotation {
  id: string;
  code: string;
  name: string;
  preset: RotationPreset;
  cycleLengthDays: number;
  steps: RotationStep[];
  isActive: boolean;
}

export type SaveRotationRequest = Omit<Rotation, "id" | "isActive"> & { isActive?: boolean };

export interface AssignRotationRequest {
  employeeIds: string[];
  /** yyyy-MM-dd. */
  startDate: string;
  endDate?: string;
  offsetDays: number;
}

export interface AssignRotationResult {
  /** Draft shift assignments generated. */
  createdCount: number;
  /** Months (yyyy-MM) skipped because their period is locked. */
  skippedLockedMonths: string[];
}

export async function listRotations(): Promise<Rotation[]> {
  const { data } = await apiClient.get<Rotation[]>(BASE);
  return data;
}

export async function saveRotation(id: string | null, dto: SaveRotationRequest): Promise<Rotation> {
  const { data } = id ? await apiClient.put<Rotation>(`${BASE}/${id}`, dto) : await apiClient.post<Rotation>(BASE, dto);
  return data;
}

/** Overlapping assignment for an employee → 409 ProblemDetails (localized `detail`). */
export async function assignRotation(id: string, dto: AssignRotationRequest): Promise<AssignRotationResult> {
  const { data } = await apiClient.post<AssignRotationResult>(`${BASE}/${id}/assignments`, dto);
  return data;
}
