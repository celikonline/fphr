import { apiClient } from "@/lib/apiClient";

/**
 * Shift module (TK-027 "vardiya görüntüleme"): mirrors `ShiftsController`'s self-service group
 * (`GET shifts/me`) and the planner grid (`GET shifts`, permission `attendance.shift.view` -
 * the query narrows results to the caller's data scope, e.g. own org unit). Also mirrors
 * `AvailabilityController`'s self-service group ("Müsait değilim": `GET/POST availability/me`,
 * `POST availability/me/{id}/cancel`) - that endpoint already existed before this card, package A5.
 * Only the read-only self-service + own-availability surface a phone needs; planning/publishing
 * (`attendance.shift.plan`/`.publish`) and the planner's per-employee availability view
 * (`attendance.availability.view`) stay web-only for now.
 */
const ATTENDANCE_BASE = "/api/v1/attendance";

export type ShiftAssignmentStatus = "draft" | "published";

/** `ShiftAssignmentDto`; `date` is `DateOnly` ("yyyy-MM-dd"), `start`/`end` are `TimeOnly` ("HH:mm:ss"). */
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

export interface DateRangeQuery {
  from: string;
  to: string;
}

/** `SubmitMyAvailabilityRequest` body; `date` is `DateOnly`, `start`/`end` are `TimeOnly` or omitted for the whole day. */
export interface SubmitAvailabilityInput {
  date: string;
  start?: string | null;
  end?: string | null;
  kind: AvailabilityKind;
  note?: string | null;
}

export class ShiftService {
  /** "Vardiyam" (package A5 self-servis) - only published shifts, drafts are planner work-in-progress. */
  async getMyShifts(query: DateRangeQuery): Promise<ShiftAssignment[]> {
    const response = await apiClient.get<ShiftAssignment[]>(`${ATTENDANCE_BASE}/shifts/me`, {
      params: query,
    });
    return response.data;
  }

  /**
   * Haftalık/aylık ızgara (`attendance.shift.view`); employeeIds omitted narrows to "everyone the
   * caller's data scope allows" (own org unit/team), never every tenant employee - see
   * `ListShiftAssignmentsHandler`. Throws (403) when the caller has no `attendance.shift.view`
   * permission at all - callers without it should catch and hide this section.
   */
  async getTeamShifts(query: DateRangeQuery): Promise<ShiftAssignment[]> {
    const response = await apiClient.get<ShiftAssignment[]>(`${ATTENDANCE_BASE}/shifts`, {
      params: query,
    });
    return response.data;
  }

  /** "Müsait değilim" (package A5) - kendi kayıtları. */
  async getMyAvailability(query: DateRangeQuery): Promise<EmployeeAvailability[]> {
    const response = await apiClient.get<EmployeeAvailability[]>(
      `${ATTENDANCE_BASE}/availability/me`,
      {
        params: query,
      }
    );
    return response.data;
  }

  async submitMyAvailability(input: SubmitAvailabilityInput): Promise<string> {
    const response = await apiClient.post<string>(`${ATTENDANCE_BASE}/availability/me`, input);
    return response.data;
  }

  async cancelMyAvailability(availabilityId: string): Promise<void> {
    await apiClient.post(`${ATTENDANCE_BASE}/availability/me/${availabilityId}/cancel`);
  }
}

export const shiftService = new ShiftService();
