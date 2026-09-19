import { describe, expect, it } from "vitest";
import type { MonthlyTimesheetRow, TeamWeekDay } from "@/services/attendance.service";
import {
  dayChipTone,
  monthStartOf,
  pendingApprovalCount,
  shiftMonth,
  summarizeAnomalies,
  totalsOf,
} from "./monthly-timesheet-helpers";

function row(overrides: Partial<MonthlyTimesheetRow> = {}): MonthlyTimesheetRow {
  return {
    employeeId: "e1",
    employeeName: "Ayşe Yılmaz",
    workedDays: 0,
    workedHours: 0,
    missingDays: 0,
    overtimeHours: 0,
    paidLeaveDays: 0,
    unpaidLeaveDays: 0,
    sickLeaveDays: 0,
    isPeriodLocked: false,
    isApproved: false,
    ...overrides,
  };
}

function day(overrides: Partial<TeamWeekDay> = {}): TeamWeekDay {
  return {
    date: "2026-09-01",
    workedMinutes: 0,
    status: "unresolved",
    anomalies: [],
    breakMinutesApplied: 0,
    nightMinutes: 0,
    dailyLimitExceeded: false,
    nightLimitExceeded: false,
    ...overrides,
  };
}

describe("month navigation", () => {
  it("normalizes to the first day and crosses year boundaries without UTC shifts", () => {
    expect(monthStartOf(new Date(2026, 8, 19)).getDate()).toBe(1);
    const back = shiftMonth(new Date(2026, 0, 31), -1);
    expect([back.getFullYear(), back.getMonth(), back.getDate()]).toEqual([2025, 11, 1]);
    const forward = shiftMonth(new Date(2026, 11, 15), 1);
    expect([forward.getFullYear(), forward.getMonth(), forward.getDate()]).toEqual([2027, 0, 1]);
  });
});

describe("totalsOf", () => {
  it("sums every column and rounds float noise away", () => {
    const totals = totalsOf([
      row({ workedHours: 0.1, workedDays: 10, sickLeaveDays: 1 }),
      row({ workedHours: 0.2, workedDays: 12.5, overtimeHours: 3.25 }),
    ]);
    expect(totals.workedHours).toBe(0.3);
    expect(totals.workedDays).toBe(22.5);
    expect(totals.overtimeHours).toBe(3.25);
    expect(totals.sickLeaveDays).toBe(1);
  });

  it("returns zeros for an empty grid", () => {
    expect(totalsOf([]).workedDays).toBe(0);
  });
});

describe("pendingApprovalCount", () => {
  it("counts only rows that are not approved yet", () => {
    expect(pendingApprovalCount([row({ isApproved: true }), row(), row()])).toBe(2);
  });
});

describe("summarizeAnomalies", () => {
  it("counts open anomaly days and the worked-while-on-leave subset, ignoring resolved/ignored days", () => {
    const summary = summarizeAnomalies([
      day({ anomalies: ["lateArrival"] }),
      day({ anomalies: ["workedWhileOnLeave"] }),
      day({ anomalies: ["workedWhileOnLeave"], status: "ignored" }),
      day({ anomalies: ["missingCheckOut"], status: "resolved" }),
      day({ workedMinutes: 480 }),
    ]);
    expect(summary).toEqual({ unresolvedDays: 2, workedWhileOnLeaveDays: 1 });
  });

  it("treats missing day data as no anomalies", () => {
    expect(summarizeAnomalies(undefined)).toEqual({ unresolvedDays: 0, workedWhileOnLeaveDays: 0 });
  });
});

describe("dayChipTone", () => {
  it.each([
    [day(), "empty"],
    [day({ workedMinutes: 480 }), "ok"],
    [day({ anomalies: ["lateArrival", "overwork"] }), "issue"],
    [day({ anomalies: ["lateArrival", "workedWhileOnLeave"] }), "leaveConflict"],
    [day({ anomalies: ["workedWhileOnLeave"], status: "ignored" }), "settled"],
  ] as const)("classifies %#", (input, tone) => {
    expect(dayChipTone(input)).toBe(tone);
  });
});
