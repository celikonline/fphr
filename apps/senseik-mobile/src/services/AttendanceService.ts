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
  | "unplannedWork"
  | "workedWhileOnLeave";

/** Splits a possibly-combined `AttendanceAnomaly` string into its individual flags ("none"/empty → []). */
export function parseAnomalyFlags(value: string): AttendanceAnomalyFlag[] {
  return value
    .split(",")
    .map((part) => part.trim())
    .filter((part) => part.length > 0 && part !== "none") as AttendanceAnomalyFlag[];
}

export type AttendanceResolutionState = "unresolved" | "resolved" | "ignored";
export type AttendanceDaySource = "manual" | "import" | "device" | "selfService";
export type AnomalyResolutionAction =
  "addOvertime" | "setWorkedHours" | "createLeaveRequest" | "ignore";

/**
 * `AttendanceDayDto`; `checkIn`/`checkOut`/`plannedStart`/`plannedEnd` are `TimeOnly` ("HH:mm:ss"), omitted when unset.
 * `onLeave` is always present; `leaveTypeName` is omitted when not on leave or the leave type is masked
 * (seeded types carry a `leave.type.*` resource key - resolve with `@/lib/leaveTypeNames`).
 * With `includeLeaveDays: true` the list also contains leave-only rows (id `"00000000-0000-0000-0000-000000000000"`,
 * `workedMinutes: 0`, `onLeave: true`) for approved-leave dates with no recorded day - key list items by `date`, not `id`.
 */
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
  onLeave?: boolean;
  leaveTypeName?: string | null;
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

/** `PunchDirection`: the break directions (Mola başı / Mola sonu) are only valid while checked in. */
export type PunchDirection = "in" | "out" | "breakStart" | "breakEnd";

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

/** One break of the day ("HH:mm:ss", tenant-local, oldest first); `end` is omitted while still open. */
export interface CheckInBreak {
  start: string;
  end?: string | null;
}

/**
 * `MyCheckInStatusDto`: "şu an içeride miyim" - which punch (Giriş/Çıkış/Mola başı/Mola sonu) comes next.
 * `breakStartedAt` is the open break's start ("HH:mm:ss"); `breakMinutesToday` counts closed breaks only.
 * `breaks` is always present (`[]` when not checked in) - the day's breaks oldest first, so it survives
 * an app restart unlike the in-session punch times.
 */
export interface MyCheckInStatus {
  isCheckedIn: boolean;
  date: string;
  checkIn?: string | null;
  checkOut?: string | null;
  onBreak?: boolean;
  breakStartedAt?: string | null;
  breakMinutesToday?: number;
  breaks?: CheckInBreak[];
}

export interface QrCheckInInput {
  code: string;
  direction: PunchDirection;
  latitude?: number;
  longitude?: number;
}

/** GPS punch: without `locationId` no geofence is checked (roaming/remote employee), the position is only recorded. */
export interface GpsCheckInInput {
  latitude: number;
  longitude: number;
  locationId?: string;
  direction: PunchDirection;
}

export class AttendanceService {
  async getMyCheckInStatus(): Promise<MyCheckInStatus> {
    const response = await apiClient.get<MyCheckInStatus>(`${ATTENDANCE_BASE}/self-checkin/status`);
    return response.data;
  }

  /** Lokasyon girişindeki QR kodun değeriyle giriş/çıkış (attendance.my.checkin); geofence'li lokasyonda konum zorunlu. */
  async qrCheckIn(input: QrCheckInInput): Promise<SelfCheckInResult> {
    const response = await apiClient.post<SelfCheckInResult>(
      `${ATTENDANCE_BASE}/self-checkin/qr`,
      input
    );
    return response.data;
  }

  /** Konumla giriş/çıkış/mola (attendance.my.checkin). */
  async gpsCheckIn(input: GpsCheckInInput): Promise<SelfCheckInResult> {
    const response = await apiClient.post<SelfCheckInResult>(
      `${ATTENDANCE_BASE}/self-checkin/gps`,
      input
    );
    return response.data;
  }

  /**
   * `includeLeaveDays` opts into synthetic leave-only rows (mobile Puantaj); omitted it matches every
   * other caller (web) byte-for-byte, only enriched with the additive `onLeave`/`leaveTypeName` fields.
   */
  async getMyAttendanceDays(
    query: DateRangeQuery,
    includeLeaveDays?: boolean
  ): Promise<AttendanceDay[]> {
    const response = await apiClient.get<AttendanceDay[]>(`${ATTENDANCE_BASE}/timesheet/me`, {
      params: includeLeaveDays ? { ...query, includeLeaveDays } : query,
    });
    return response.data;
  }

  async getMyTimesheetSummary(query: DateRangeQuery): Promise<MyTimesheetSummary> {
    const response = await apiClient.get<MyTimesheetSummary>(
      `${ATTENDANCE_BASE}/timesheet/me/summary`,
      { params: query }
    );
    return response.data;
  }
}

export const attendanceService = new AttendanceService();
