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

/** `TeamWeekDayDto` (ekip haftalık ızgarası hücresi); `anomalies` is a flag array here, unlike `AttendanceDay.anomalies`. */
export interface TeamWeekDay {
  date: string;
  attendanceDayId?: string | null;
  workedMinutes: number;
  status: AttendanceResolutionState;
  anomalies: AttendanceAnomalyFlag[];
}

/** `TeamWeekRowDto` (S-8): one employee's Monday–Sunday row for the manager's weekly grid. */
export interface TeamWeekRow {
  employeeId: string;
  employeeName: string;
  days: TeamWeekDay[];
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

/** Ekip haftalık puantaj ızgarası (attendance.timesheet.view + veri kapsamı; `orgUnitId` opsiyonel daraltma). */
export async function getTeamTimesheet(weekStart: string, orgUnitId?: string | null): Promise<TeamWeekRow[]> {
  const { data } = await apiClient.get<TeamWeekRow[]>(`${ATTENDANCE_BASE}/timesheet/team`, {
    params: { weekStart, orgUnitId: orgUnitId || undefined },
  });
  return data;
}

// ---------------------------------------------------------------- Müsaitlik / Availability

/** `AvailabilityKind` (package A5, 11.3.6 "müsait değilim"). */
export type AvailabilityKind = "unavailable" | "weeklyRestRequest";

export type AvailabilityStatus = "active" | "cancelled";

/** `EmployeeAvailabilityDto`; `start`/`end` null on both means the whole day. */
export interface EmployeeAvailability {
  id: string;
  employeeId: string;
  date: string;
  start?: string | null;
  end?: string | null;
  kind: AvailabilityKind;
  note?: string | null;
  status: AvailabilityStatus;
}

/** "Müsaitliğim" listesi (attendance.availability.self). */
export async function getMyAvailability(query: DateRangeQuery): Promise<EmployeeAvailability[]> {
  const { data } = await apiClient.get<EmployeeAvailability[]>(`${ATTENDANCE_BASE}/availability/me`, { params: query });
  return data;
}

export interface SubmitAvailabilityInput {
  date: string;
  start: string | null;
  end: string | null;
  kind: AvailabilityKind;
  note: string | null;
}

/** "Müsait değilim bildir" (attendance.availability.self). */
export async function submitMyAvailability(input: SubmitAvailabilityInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${ATTENDANCE_BASE}/availability/me`, input);
  return data;
}

/** attendance.availability.self. */
export async function cancelMyAvailability(id: string): Promise<void> {
  await apiClient.post(`${ATTENDANCE_BASE}/availability/me/${id}/cancel`);
}

/** Planlayıcı görünümü: bir çalışanın müsaitlik girişleri (attendance.availability.view). */
export async function getEmployeeAvailability(employeeId: string, query: DateRangeQuery): Promise<EmployeeAvailability[]> {
  const { data } = await apiClient.get<EmployeeAvailability[]>(`${ATTENDANCE_BASE}/availability/employees/${employeeId}`, { params: query });
  return data;
}

/** Bir satırın içe alınamama nedeni (S-11); `code` `attendance.device_log.*` biçiminde, FE'de yerelleştirilir. */
export interface DeviceLogImportError {
  line: number;
  code: string;
  value?: string | null;
}

/** `DeviceLogImportResultDto` (S-11): satır bazında atlama, tüm dosya reddedilmez. */
export interface DeviceLogImportResult {
  imported: number;
  skipped: number;
  errors: DeviceLogImportError[];
}

/** PDKS cihaz kaydı CSV içe aktarımı (attendance.timesheet.import); multipart tek dosya alanı "file". */
export async function importDeviceLogs(file: File): Promise<DeviceLogImportResult> {
  const form = new FormData();
  form.append("file", file);
  const { data } = await apiClient.post<DeviceLogImportResult>(`${ATTENDANCE_BASE}/device-logs/import`, form, {
    headers: { "Content-Type": "multipart/form-data" },
  });
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
  /** Otomatik kilit ayarı açıksa hesaplanan kilitlenme tarihi ("yyyy-MM-dd"); ayar kapalıysa alan düşer (BE md.4). */
  scheduledLockDate?: string | null;
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

// ---------------------------------------------------------------- Çalışma takvimleri / Work Schedules

/** .NET `DayOfWeek` serialized camelCase; Sunday-first like the API, UI renders Monday-first. */
export type WeekDay = "sunday" | "monday" | "tuesday" | "wednesday" | "thursday" | "friday" | "saturday";

export const WEEK_DAYS_MONDAY_FIRST: WeekDay[] = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

/** `WorkScheduleDayDto`. `start`/`end`/`breakStart`/`breakEnd` are `TimeOnly` ("HH:mm:ss"), null when not a working day. */
export interface WorkScheduleDay {
  dayOfWeek: WeekDay;
  isWorkingDay: boolean;
  start?: string | null;
  end?: string | null;
  breakStart?: string | null;
  breakEnd?: string | null;
}

/** `WorkScheduleDto`. */
export interface WorkSchedule {
  id: string;
  name: string;
  isDefault: boolean;
  isActive: boolean;
  includeSaturdayWhenFullWeek: boolean;
  weeklyMinutes: number;
  assignedEmployeeCount: number;
  days: WorkScheduleDay[];
}

export interface WorkScheduleDaysInput {
  days: WorkScheduleDay[];
  includeSaturdayWhenFullWeek: boolean;
}

/** attendance.workschedule.view. */
export async function listWorkSchedules(includeInactive = false): Promise<WorkSchedule[]> {
  const { data } = await apiClient.get<WorkSchedule[]>(`${ATTENDANCE_BASE}/work-schedules`, { params: { includeInactive } });
  return data;
}

export async function getWorkSchedule(id: string): Promise<WorkSchedule> {
  const { data } = await apiClient.get<WorkSchedule>(`${ATTENDANCE_BASE}/work-schedules/${id}`);
  return data;
}

/** "Yeni çalışma takvimi ekle" (attendance.workschedule.manage). */
export async function createWorkSchedule(input: { name: string } & WorkScheduleDaysInput): Promise<string> {
  const { data } = await apiClient.post<string>(`${ATTENDANCE_BASE}/work-schedules`, input);
  return data;
}

export async function renameWorkSchedule(id: string, name: string): Promise<void> {
  await apiClient.put(`${ATTENDANCE_BASE}/work-schedules/${id}/name`, { name });
}

export async function updateWorkScheduleDays(id: string, input: WorkScheduleDaysInput): Promise<void> {
  await apiClient.put(`${ATTENDANCE_BASE}/work-schedules/${id}/days`, input);
}

/** "Varsayılan yap": exactly one default schedule remains per tenant afterwards. */
export async function setDefaultWorkSchedule(id: string): Promise<void> {
  await apiClient.post(`${ATTENDANCE_BASE}/work-schedules/${id}/set-default`);
}

export async function setWorkScheduleActive(id: string, isActive: boolean): Promise<void> {
  await apiClient.put(`${ATTENDANCE_BASE}/work-schedules/${id}/active`, { isActive });
}

/** Fails (Conflict) when the schedule is the default one or still has assigned employees. */
export async function deleteWorkSchedule(id: string): Promise<void> {
  await apiClient.delete(`${ATTENDANCE_BASE}/work-schedules/${id}`);
}

/** "Çalışanlara ata" (attendance.workschedule.assign); each employee gets a new dated assignment. */
export async function assignWorkSchedule(id: string, employeeIds: string[], effectiveFrom: string): Promise<void> {
  await apiClient.post(`${ATTENDANCE_BASE}/work-schedules/${id}/assign`, { employeeIds, effectiveFrom });
}

/** `WorkScheduleAssignmentSource` (11.3.6: tekil atama / toplu işlem / işe alım varsayılanı). */
export type WorkScheduleAssignmentSource = "manual" | "bulk" | "hire";

/**
 * `EmployeeWorkScheduleAssignmentDto`: one dated row in the çalışan kartı "Kariyer &gt; Çalışma Takvimi" history.
 * `effectiveTo` is derived server-side (the next assignment's start minus one day); null on the ongoing row.
 */
export interface EmployeeWorkScheduleAssignment {
  id: string;
  workScheduleId: string;
  workScheduleName: string;
  effectiveFrom: string;
  effectiveTo?: string | null;
  isCurrent: boolean;
  source: WorkScheduleAssignmentSource;
  assignedAt: string;
}

/** Çalışan kartı "Kariyer &gt; Çalışma Takvimi" (attendance.workschedule.view), newest first. */
export async function getEmployeeWorkScheduleAssignments(employeeId: string): Promise<EmployeeWorkScheduleAssignment[]> {
  const { data } = await apiClient.get<EmployeeWorkScheduleAssignment[]>(`${ATTENDANCE_BASE}/work-schedules/employees/${employeeId}/assignments`);
  return data;
}

/** Own card, self-service (attendance.my.workschedule). */
export async function getMyWorkScheduleAssignments(): Promise<EmployeeWorkScheduleAssignment[]> {
  const { data } = await apiClient.get<EmployeeWorkScheduleAssignment[]>(`${ATTENDANCE_BASE}/work-schedules/me/assignments`);
  return data;
}
