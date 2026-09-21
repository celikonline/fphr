import { apiClient } from "@/lib/apiClient";

/**
 * Attendance module (self-service "Puantajım"): mirrors `TimesheetController`'s `/attendance/timesheet/me`
 * group (`GET timesheet/me`, `GET timesheet/me/summary`) - the same endpoints
 * `web/src/services/attendance.service.ts` calls. Only the read-only self-service surface a phone needs:
 * the caller's own recorded days + period totals. Manual correction ("Çalışılan saat"), anomaly resolution
 * (Fazla mesai ekle/İzin talebi/Ignore) and any employee-scoped/admin endpoint stay web-only for now.
 */
const ATTENDANCE_BASE = "/api/v1/attendance";

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
    .map(part => part.trim())
    .filter(part => part.length > 0 && part !== "none") as AttendanceAnomalyFlag[];
}

export type AttendanceResolutionState = "unresolved" | "resolved" | "ignored";
export type AttendanceDaySource = "manual" | "import" | "device";
export type AnomalyResolutionAction = "addOvertime" | "setWorkedHours" | "createLeaveRequest" | "ignore";

/** `AttendanceDayDto`; `checkIn`/`checkOut`/`plannedStart`/`plannedEnd` are `TimeOnly` ("HH:mm:ss"), omitted when unset. */
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

/** `MyTimesheetSummaryDto` (package A5 "Puantajım" özet kartları). */
export interface MyTimesheetSummary {
  workedDays: number;
  workedMinutes: number;
}

export interface DateRangeQuery {
  from: string;
  to: string;
}

export type PunchDirection = "in" | "out";

/** `SelfCheckInResultDto`. */
export interface SelfCheckInResult {
  attendanceDayId: string;
  date: string;
  direction: PunchDirection;
  time: string;
  locationId?: string | null;
  locationName?: string | null;
  withinGeofence?: boolean | null;
}

/** `MyCheckInStatusDto`: "şu an içeride miyim" - Giriş mi Çıkış mı yapılacağını belirler. */
export interface MyCheckInStatus {
  isCheckedIn: boolean;
  date: string;
  checkIn?: string | null;
  checkOut?: string | null;
}

export interface QrCheckInInput {
  code: string;
  direction: PunchDirection;
  latitude?: number;
  longitude?: number;
}

export class AttendanceService {
  async getMyCheckInStatus(): Promise<MyCheckInStatus> {
    const response = await apiClient.get<MyCheckInStatus>(`${ATTENDANCE_BASE}/self-checkin/status`);
    return response.data;
  }

  /** Lokasyon girişindeki QR kodun değeriyle giriş/çıkış (attendance.my.checkin); geofence'li lokasyonda konum zorunlu. */
  async qrCheckIn(input: QrCheckInInput): Promise<SelfCheckInResult> {
    const response = await apiClient.post<SelfCheckInResult>(`${ATTENDANCE_BASE}/self-checkin/qr`, input);
    return response.data;
  }

  async getMyAttendanceDays(query: DateRangeQuery): Promise<AttendanceDay[]> {
    const response = await apiClient.get<AttendanceDay[]>(`${ATTENDANCE_BASE}/timesheet/me`, { params: query });
    return response.data;
  }

  async getMyTimesheetSummary(query: DateRangeQuery): Promise<MyTimesheetSummary> {
    const response = await apiClient.get<MyTimesheetSummary>(`${ATTENDANCE_BASE}/timesheet/me/summary`, { params: query });
    return response.data;
  }
}

export const attendanceService = new AttendanceService();
