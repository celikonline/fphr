/**
 * Vardiya takvimi toplu işlemleri (Z3.1, B8): `ShiftsController` altındaki bulk-assign / bulk-clear / unassigned /
 * totals aksiyonları (attendance.shift.plan / attendance.shift.view). Tarihler `DateOnly` (yyyy-MM-dd).
 */
import { apiClient } from "@senseik/api-client";
import type { ShiftAssignment } from "./attendance.service";

const BASE = "/v1/attendance/shifts";

/** `ShiftDayType` (camelCase enum). `work` dışındaki günlerde saat alanları yok sayılır. */
export type ShiftDayType = "work" | "weeklyRest" | "restDay" | "publicHoliday";

export const SHIFT_DAY_TYPES: readonly ShiftDayType[] = ["work", "weeklyRest", "restDay", "publicHoliday"];

/** `ShiftAssignmentDto` + Z0.2 ek alanları (eski kayıtlarda yok olabilir). */
export type CalendarShiftAssignment = ShiftAssignment & {
  shiftDefinitionId?: string | null;
  dayType?: ShiftDayType | null;
};

export interface BulkAssignShiftsInput {
  employeeIds: string[];
  /** Hücre bazlı tarih listesi (seçim dikdörtgeni × tarihler). */
  dates: string[];
  /** `dayType === "work"` iken zorunlu. */
  shiftDefinitionId?: string;
  dayType: ShiftDayType;
  /** Yayınlanmış vardiyanın üzerine yazılsın mı (varsayılan false: yalnız taslak ezilir). */
  overwritePublished?: boolean;
}

export interface SkippedShiftCell {
  employeeId: string;
  date: string;
  /** Örn. `locked` | `published`. */
  reason: string;
}

export interface BulkShiftResult {
  affectedCount: number;
  skipped: SkippedShiftCell[];
}

export interface BulkClearShiftsInput {
  employeeIds: string[];
  dates: string[];
  overwritePublished?: boolean;
}

export interface UnassignedShiftQuery {
  from: string;
  to: string;
  employeeIds?: string[];
}

export interface ShiftDayTotal {
  date: string;
  employeeCount: number;
  totalMinutes: number;
}

export interface ShiftPlanTotals {
  byDate: ShiftDayTotal[];
  byEmployee: { employeeId: string; totalMinutes: number }[];
}

export async function bulkAssignShifts(input: BulkAssignShiftsInput): Promise<BulkShiftResult> {
  const { data } = await apiClient.post<BulkShiftResult>(`${BASE}/bulk-assign`, input);
  return data;
}

export async function bulkClearShifts(input: BulkClearShiftsInput): Promise<BulkShiftResult> {
  const { data } = await apiClient.post<BulkShiftResult>(`${BASE}/bulk-clear`, input);
  return data;
}

/** Aralıkta hiç vardiyası olmayan çalışanların kimlikleri. */
export async function listUnassignedEmployeeIds(query: UnassignedShiftQuery): Promise<string[]> {
  const { data } = await apiClient.get<string[]>(`${BASE}/unassigned`, {
    params: { from: query.from, to: query.to, employeeIds: query.employeeIds },
  });
  return data;
}

export async function getShiftPlanTotals(query: UnassignedShiftQuery): Promise<ShiftPlanTotals> {
  const { data } = await apiClient.get<ShiftPlanTotals>(`${BASE}/totals`, {
    params: { from: query.from, to: query.to, employeeIds: query.employeeIds },
  });
  return data;
}
