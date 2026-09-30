import type { TFunction } from "i18next";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";

export const PERIOD_LOCKED_CODE = "attendance.timesheet.period_locked";

/** API error text for attendance writes: known codes are translated client-side, others use the server detail. */
export function getAttendanceErrorMessage(error: unknown, fallback: string, t: TFunction | ((key: string) => string)): string {
  if (getApiProblem(error)?.code === PERIOD_LOCKED_CODE) return (t as (key: string) => string)("attendance:errors.periodLocked");
  return getApiErrorMessage(error, fallback);
}
