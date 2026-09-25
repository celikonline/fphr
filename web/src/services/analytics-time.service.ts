/**
 * Zaman KPI'ları (zaman-devam Z1.3, B5): mirrors `TimeKpisController` (`/v1/analytics/kpis/...`). All four endpoints are
 * scoped server-side by the caller's `IDataScopeService` scope; an empty scope returns empty lists / zero totals, not an
 * error. Response shapes below are the F3 client contract - B5 must serialize the same camelCase fields.
 */
import { apiClient } from "@senseik/api-client";
import type { DailyAttendanceTotals } from "@/services/attendance-daily.service";

const KPI_BASE = "/v1/analytics/kpis";

export type OvertimeGroupBy = "branch" | "unit" | "team" | "title";

export interface TimeKpiRangeParams {
  /** "yyyy-MM-dd", inclusive. Omitted = the API default (last 7 days / current month). */
  from?: string;
  to?: string;
}

/** One row of `late-early-by-unit`. */
export interface LateEarlyByUnitRow {
  orgUnitId: string;
  orgUnitName: string;
  lateCount: number;
  lateMinutes: number;
  earlyLeaveCount: number;
  earlyLeaveMinutes: number;
}

/** One row of `overtime-by-group`; `groupName` is the branch / unit / team / title name. */
export interface OvertimeByGroupRow {
  groupId?: string;
  groupName: string;
  overtimeMinutes: number;
  employeeCount: number;
}

/** `shift-attendance-rate`: worked / planned shift days; `rate` is 0..1 (0 when nothing was planned). */
export interface ShiftAttendanceRate {
  plannedShiftDays: number;
  workedShiftDays: number;
  rate: number;
}

export async function getAttendanceSevenDay(params: TimeKpiRangeParams = {}): Promise<DailyAttendanceTotals[]> {
  const { data } = await apiClient.get<DailyAttendanceTotals[]>(`${KPI_BASE}/attendance-7d`, { params });
  return data;
}

export async function getLateEarlyByUnit(params: TimeKpiRangeParams = {}): Promise<LateEarlyByUnitRow[]> {
  const { data } = await apiClient.get<LateEarlyByUnitRow[]>(`${KPI_BASE}/late-early-by-unit`, { params });
  return data;
}

export async function getOvertimeByGroup(
  groupBy: OvertimeGroupBy,
  params: TimeKpiRangeParams = {},
): Promise<OvertimeByGroupRow[]> {
  const { data } = await apiClient.get<OvertimeByGroupRow[]>(`${KPI_BASE}/overtime-by-group`, {
    params: { ...params, groupBy },
  });
  return data;
}

export async function getShiftAttendanceRate(params: TimeKpiRangeParams = {}): Promise<ShiftAttendanceRate> {
  const { data } = await apiClient.get<ShiftAttendanceRate>(`${KPI_BASE}/shift-attendance-rate`, { params });
  return data;
}
