import type { MonthlyTimesheetRow, TeamWeekDay } from "@/services/attendance.service";

/** First day of the calendar month that contains `date` (local time, no UTC shift). */
export function monthStartOf(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

/** First day of the month `delta` months after (or before, when negative) the month of `date`. */
export function shiftMonth(date: Date, delta: number): Date {
  return new Date(date.getFullYear(), date.getMonth() + delta, 1);
}

export interface MonthlyTotals {
  workedDays: number;
  workedHours: number;
  missingDays: number;
  overtimeHours: number;
  paidLeaveDays: number;
  unpaidLeaveDays: number;
  sickLeaveDays: number;
}

/** Column sums for the footer row. Rounded to 2 decimals so float noise (0.1 + 0.2) never shows up. */
export function totalsOf(rows: readonly MonthlyTimesheetRow[]): MonthlyTotals {
  const sum = (pick: (row: MonthlyTimesheetRow) => number) => Math.round(rows.reduce((acc, row) => acc + pick(row), 0) * 100) / 100;
  return {
    workedDays: sum((r) => r.workedDays),
    workedHours: sum((r) => r.workedHours),
    missingDays: sum((r) => r.missingDays),
    overtimeHours: sum((r) => r.overtimeHours),
    paidLeaveDays: sum((r) => r.paidLeaveDays),
    unpaidLeaveDays: sum((r) => r.unpaidLeaveDays),
    sickLeaveDays: sum((r) => r.sickLeaveDays),
  };
}

/** Rows "Tümünü Onayla" would still touch (the server skips the already approved ones). */
export function pendingApprovalCount(rows: readonly MonthlyTimesheetRow[]): number {
  return rows.filter((row) => !row.isApproved).length;
}

export interface DayAnomalySummary {
  /** Days that still carry at least one unresolved anomaly flag. */
  unresolvedDays: number;
  /** Days flagged `workedWhileOnLeave` that are not yet resolved/ignored (leave cross-check, senseik-029). */
  workedWhileOnLeaveDays: number;
}

/** Anomaly counters for one employee's month; resolved/ignored days no longer count as open problems. */
export function summarizeAnomalies(days: readonly TeamWeekDay[] | undefined): DayAnomalySummary {
  const open = (days ?? []).filter((day) => day.status === "unresolved" && day.anomalies.length > 0);
  return {
    unresolvedDays: open.length,
    workedWhileOnLeaveDays: open.filter((day) => day.anomalies.includes("workedWhileOnLeave")).length,
  };
}

export type DayChipTone = "empty" | "ok" | "issue" | "leaveConflict" | "settled";

/** Colour bucket of one day cell: leave conflict > open anomaly > settled anomaly > worked > empty. */
export function dayChipTone(day: TeamWeekDay): DayChipTone {
  const hasFlags = day.anomalies.length > 0;
  if (hasFlags && day.status === "unresolved") {
    return day.anomalies.includes("workedWhileOnLeave") ? "leaveConflict" : "issue";
  }

  if (hasFlags) return "settled";
  return day.workedMinutes > 0 ? "ok" : "empty";
}
