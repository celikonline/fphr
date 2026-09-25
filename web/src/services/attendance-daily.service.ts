/**
 * Zaman-devam günlük liste servisi (spec §5.4/§7): mirrors `IDailyAttendanceQuery`, `IPunchRecorder` and
 * `IAttendanceDailyKpiReader` from `Sense.Modules.Attendance.Contracts` (W0-b). Enums serialize as camelCase strings,
 * `DateOnly` as "yyyy-MM-dd", `TimeOnly` as "HH:mm:ss", null fields may be omitted by the API.
 */
import { apiClient } from "@senseik/api-client";
import type { PagedResult } from "@/types";

const ATTENDANCE_BASE = "/v1/attendance";

/** `DailyStatus`. */
export type DailyStatus =
  | "incalculable"
  | "arrived"
  | "late"
  | "earlyLeave"
  | "absent"
  | "overtime"
  | "undertime"
  | "onLeave";

/** `PunchDirectionDto`. */
export type PunchDirection = "in" | "out" | "unknown";

/** `PunchSourceDto`. */
export type PunchSource = "manual" | "import" | "device" | "selfService" | "request" | "terminal";

/** `PunchInput`. */
export interface PunchInput {
  tenantId: string;
  employeeId: string;
  /** ISO 8601 UTC. */
  occurredAtUtc: string;
  direction: PunchDirection;
  source: PunchSource;
  sourceRef?: string | null;
  terminalId?: string | null;
  locationId?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  isManual: boolean;
  createdByUserId?: string | null;
  reason?: string | null;
}

/** Manual punch request body: the server fills tenant/user/source, so only the caller-provided fields remain. */
export type ManualPunchInput = Pick<PunchInput, "employeeId" | "occurredAtUtc" | "direction"> & { reason: string };

/** `DailyAttendanceRowDto`. */
export interface DailyAttendanceRow {
  employeeId: string;
  employeeName: string;
  orgUnitName?: string | null;
  date: string;
  checkIn?: string | null;
  checkOut?: string | null;
  workedMinutes: number;
  overtimeMinutes: number;
  anomalies: string[];
  leaveType?: string | null;
}

/** `DailyAttendanceTotalsDto`. */
export interface DailyAttendanceTotals {
  date: string;
  planned: number;
  arrived: number;
  late: number;
  earlyLeave: number;
  absent: number;
  onLeave: number;
}

/** `EmployeeAttendancePeriodDto`. */
export interface EmployeeAttendancePeriod {
  employeeId: string;
  lateCount: number;
  lateMinutes: number;
  earlyLeaveCount: number;
  earlyLeaveMinutes: number;
  overtimeMinutes: number;
  plannedShiftDays: number;
  workedShiftDays: number;
}

/** `EmployeeLeaveOnDate` (Leave.Contracts). */
export interface EmployeeLeaveOnDate {
  employeeId: string;
  leaveTypeCode: string;
  isPaid: boolean;
}

/** Day list for one status tab (attendance.daily.view). */
export async function listDaily(
  date: string,
  status: DailyStatus,
  orgUnitId?: string,
  page = 1,
): Promise<PagedResult<DailyAttendanceRow>> {
  const { data } = await apiClient.get<PagedResult<DailyAttendanceRow>>(`${ATTENDANCE_BASE}/daily`, {
    params: { date, status, orgUnitId, page },
  });
  return data;
}

/** Per-status head counts for the tab badges; statuses without rows may be omitted. */
export async function getDailyCounts(date: string, orgUnitId?: string): Promise<Partial<Record<DailyStatus, number>>> {
  const { data } = await apiClient.get<Partial<Record<DailyStatus, number>>>(`${ATTENDANCE_BASE}/daily/counts`, {
    params: { date, orgUnitId },
  });
  return data;
}

/** Adds a manual punch (attendance.punch.manage); returns the new punch id. */
export async function addManualPunch(input: ManualPunchInput): Promise<string> {
  const { data } = await apiClient.post<{ id: string }>(`${ATTENDANCE_BASE}/punches`, input);
  return data.id;
}

/** Soft-deletes a punch with a mandatory reason (attendance.punch.manage). */
export async function deletePunch(id: string, reason: string): Promise<void> {
  await apiClient.delete(`${ATTENDANCE_BASE}/punches/${id}`, { data: { reason } });
}
