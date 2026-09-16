/**
 * Attendance module: mirrors `TimesheetController` (`api/v1/attendance/timesheet`) and `ShiftsController`
 * (`api/v1/attendance/shifts`). Self-service reads need `attendance.my.timesheet` / `attendance.shift.self`;
 * the employee-scoped/admin endpoints need `attendance.timesheet.*` / `attendance.shift.*`. Dates are `DateOnly`
 * ("yyyy-MM-dd"), times are `TimeOnly` ("HH:mm:ss"). Null fields may be omitted by the API.
 */
import { apiClient } from "@senseik/api-client";

const ATTENDANCE_BASE = "/v1/attendance";

/** `AttendanceAnomaly` ([Flags]): the API serializes multiple set flags as a comma-separated string ("lateArrival, overwork"). */
export type AttendanceAnomalyFlag =
  | "noData"
  | "missingCheckIn"
  | "missingCheckOut"
  | "lateArrival"
  | "earlyLeave"
  | "lateLeave"
  | "overwork"
  | "underwork"
  | "unplannedWork";

/** Splits a possibly-combined `AttendanceAnomaly` string into its individual flags ("none"/empty → []). */
export function parseAnomalyFlags(value: string): AttendanceAnomalyFlag[] {
  return value
    .split(",")
    .map((part) => part.trim())
    .filter((part) => part.length > 0 && part !== "none") as AttendanceAnomalyFlag[];
}

export type AttendanceResolutionState = "unresolved" | "resolved" | "ignored";
export type AttendanceDaySource = "manual" | "import" | "device";
export type AnomalyResolutionAction = "addOvertime" | "setWorkedHours" | "createLeaveRequest" | "ignore";

/** `AttendanceDayDto`. */
export interface AttendanceDay {
  id: string;
  employeeId: string;
  date: string;
  checkIn?: string | null;
  checkOut?: string | null;
  plannedStart?: string | null;
  plannedEnd?: string | null;
  plannedMinutes: number;
  workedMinutes: number;
  anomalies: string;
  resolutionState: AttendanceResolutionState;
  source: AttendanceDaySource;
  allowedActions: AnomalyResolutionAction[];
}

/** `MyTimesheetSummaryDto` (package A5 "Puantajım" summary cards). */
export interface MyTimesheetSummary {
  workedDays: number;
  workedMinutes: number;
}

export type TimesheetWeekStatus = "open" | "hasIssues" | "completed";

/** `TimesheetWeekSummaryDto` ("28 Ekim haftası" haftalık ızgara başlığı). */
export interface TimesheetWeekSummary {
  employeeId: string;
  weekStart: string;
  weekEnd: string;
  status: TimesheetWeekStatus;
  workingDayCount: number;
  recordedWorkingDayCount: number;
  unresolvedDayCount: number;
}

/** `AnomalyResolutionDto` (hücre çekmecesi hareket geçmişi). */
export interface AnomalyResolution {
  id: string;
  attendanceDayId: string;
  anomaly: string;
  action: AnomalyResolutionAction;
  linkedEntityType?: string | null;
  linkedEntityId?: string | null;
  resolvedByUserId?: string | null;
  resolvedAt: string;
}

export interface DateRangeQuery {
  from: string;
  to: string;
}

// ---------------------------------------------------------------- Puantaj / Timesheet

export async function getMyAttendanceDays(query: DateRangeQuery): Promise<AttendanceDay[]> {
  const { data } = await apiClient.get<AttendanceDay[]>(`${ATTENDANCE_BASE}/timesheet/me`, { params: query });
  return data;
}

export async function getMyTimesheetSummary(query: DateRangeQuery): Promise<MyTimesheetSummary> {
  const { data } = await apiClient.get<MyTimesheetSummary>(`${ATTENDANCE_BASE}/timesheet/me/summary`, { params: query });
  return data;
}

export async function getEmployeeAttendanceDays(employeeId: string, query: DateRangeQuery): Promise<AttendanceDay[]> {
  const { data } = await apiClient.get<AttendanceDay[]>(`${ATTENDANCE_BASE}/timesheet/employees/${employeeId}`, { params: query });
  return data;
}

export async function getTimesheetWeekSummary(employeeId: string, weekStart: string): Promise<TimesheetWeekSummary> {
  const { data } = await apiClient.get<TimesheetWeekSummary>(`${ATTENDANCE_BASE}/timesheet/employees/${employeeId}/week-summary`, {
    params: { weekStart },
  });
  return data;
}

/** "Çalışılan saat" manual kayıt/düzeltme (attendance.timesheet.edit). */
export async function recordAttendance(employeeId: string, input: { date: string; checkIn: string | null; checkOut: string | null }): Promise<string> {
  const { data } = await apiClient.post<string>(`${ATTENDANCE_BASE}/timesheet/employees/${employeeId}/record`, input);
  return data;
}

export async function listAnomalyResolutions(attendanceDayId: string): Promise<AnomalyResolution[]> {
  const { data } = await apiClient.get<AnomalyResolution[]>(`${ATTENDANCE_BASE}/timesheet/days/${attendanceDayId}/resolutions`);
  return data;
}

/** "Çalışılan saat" önerisi (attendance.timesheet.resolve). */
export async function resolveWorkedHours(attendanceDayId: string, input: { workedMinutes: number; reason: string | null }): Promise<string> {
  const { data } = await apiClient.post<string>(`${ATTENDANCE_BASE}/timesheet/days/${attendanceDayId}/resolve/worked-hours`, input);
  return data;
}

/** "Fazla mesai ekle" önerisi (attendance.timesheet.resolve + leave.*). */
export async function resolveWithOvertime(attendanceDayId: string, input: { startTime: string; durationMinutes: number; description: string | null }): Promise<string> {
  const { data } = await apiClient.post<string>(`${ATTENDANCE_BASE}/timesheet/days/${attendanceDayId}/resolve/overtime`, input);
  return data;
}

/** "İzin talebi" önerisi (attendance.timesheet.resolve + leave.*). */
export async function resolveWithLeaveRequest(attendanceDayId: string, input: { leaveTypeId: string; description: string | null }): Promise<string> {
  const { data } = await apiClient.post<string>(`${ATTENDANCE_BASE}/timesheet/days/${attendanceDayId}/resolve/leave-request`, input);
  return data;
}

/** Çipleri elle temizle (öneri uygulamadan). */
export async function ignoreAnomaly(attendanceDayId: string): Promise<string> {
  const { data } = await apiClient.post<string>(`${ATTENDANCE_BASE}/timesheet/days/${attendanceDayId}/resolve/ignore`);
  return data;
}

// ---------------------------------------------------------------- Vardiya / Shifts

export type ShiftAssignmentStatus = "draft" | "published";

/** `ShiftAssignmentDto`. */
export interface ShiftAssignment {
  id: string;
  employeeId: string;
  date: string;
  start: string;
  end: string;
  breakMinutes: number;
  isNight: boolean;
  note?: string | null;
  status: ShiftAssignmentStatus;
  publishedAt?: string | null;
  shiftMinutes: number;
}

/** `ShiftSettingsDto` (tenant-wide daily/weekly limits, night work, break, auto-lock). */
export interface ShiftSettings {
  maxDailyMinutes: number;
  maxWeeklyDays: number;
  maxWeeklyMinutes: number;
  nightWorkEnabled: boolean;
  nightWorkStart?: string | null;
  nightWorkEnd?: string | null;
  nightWorkMaxMinutes?: number | null;
  breakMinMinutes: number;
  breakIsPaid: boolean;
  autoLockEnabled: boolean;
  autoLockDayOfMonth: number;
  autoLockNotifyDaysBefore: number;
}

export type ShiftSettingsInput = ShiftSettings;

export async function getShiftSettings(): Promise<ShiftSettings> {
  const { data } = await apiClient.get<ShiftSettings>(`${ATTENDANCE_BASE}/shifts/settings`);
  return data;
}

export async function updateShiftSettings(input: ShiftSettingsInput): Promise<void> {
  await apiClient.put(`${ATTENDANCE_BASE}/shifts/settings`, input);
}

/** Haftalık/aylık vardiya ızgarası; employeeIds boşsa tüm çalışanlar (attendance.shift.view). */
export async function listShiftAssignments(query: DateRangeQuery & { employeeIds?: string[] }): Promise<ShiftAssignment[]> {
  const { data } = await apiClient.get<ShiftAssignment[]>(ATTENDANCE_BASE + "/shifts", {
    params: { from: query.from, to: query.to, employeeIds: query.employeeIds },
  });
  return data;
}

export async function getUnpublishedShiftCount(query: DateRangeQuery): Promise<number> {
  const { data } = await apiClient.get<number>(`${ATTENDANCE_BASE}/shifts/unpublished-count`, { params: query });
  return data;
}

export interface AssignShiftInput {
  employeeId: string;
  date: string;
  start: string;
  end: string;
  breakMinutes: number;
  isNight: boolean;
  note: string | null;
}

/** Haftalık görünümde çalışana vardiya gir (upsert; attendance.shift.plan). */
export async function assignShift(input: AssignShiftInput): Promise<string> {
  const { data } = await apiClient.post<string>(ATTENDANCE_BASE + "/shifts", input);
  return data;
}

/** "Tekrarla" (attendance.shift.plan). */
export async function repeatShiftAssignments(input: { sourceWeekStart: string; targetWeekStarts: string[]; employeeIds: string[] }): Promise<void> {
  await apiClient.post(`${ATTENDANCE_BASE}/shifts/repeat`, input);
}

/** "Yayınla" (attendance.shift.publish). */
export async function publishShiftAssignments(assignmentIds: string[]): Promise<void> {
  await apiClient.post(`${ATTENDANCE_BASE}/shifts/publish`, { assignmentIds });
}

/** `ShiftPlanLockStatusDto`. */
export interface ShiftPlanLock {
  periodMonth: string;
  isLocked: boolean;
  lockedAt?: string | null;
  unlockedAt?: string | null;
  unlockReason?: string | null;
}

export async function getShiftPlanLockStatus(periodMonth: string): Promise<ShiftPlanLock> {
  const { data } = await apiClient.get<ShiftPlanLock>(`${ATTENDANCE_BASE}/shifts/periods/${periodMonth}/lock`);
  return data;
}

/** attendance.shift.lock. */
export async function lockShiftPeriod(periodMonth: string): Promise<void> {
  await apiClient.post(`${ATTENDANCE_BASE}/shifts/periods/${periodMonth}/lock`);
}

/** İstisnai açma, gerekçe zorunlu (attendance.shift.unlock). */
export async function unlockShiftPeriod(periodMonth: string, reason: string): Promise<void> {
  await apiClient.post(`${ATTENDANCE_BASE}/shifts/periods/${periodMonth}/unlock`, { reason });
}

/** "Vardiyam" (package A5 self-servis). */
export async function getMyShiftAssignments(query: DateRangeQuery): Promise<ShiftAssignment[]> {
  const { data } = await apiClient.get<ShiftAssignment[]>(`${ATTENDANCE_BASE}/shifts/me`, { params: query });
  return data;
}
