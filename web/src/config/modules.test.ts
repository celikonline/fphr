import { describe, expect, it } from "vitest";
import { isToggleableModule, moduleForPath } from "./modules";

describe("moduleForPath (senseik-057)", () => {
  it.each([
    ["/app/leave", "leave"],
    ["/app/leave/calendar", "leave"],
    ["/app/settings/leave/rule-sets", "leave"],
    ["/app/attendance/timesheet", "attendance"],
    ["/app/settings/rules/work-calendars", "attendance"],
    ["/app/payroll/periods/42", "payroll"],
    ["/app/recruitment/postings", "recruitment"],
    ["/app/payments", "expense"],
    ["/app/settings/company/asset-categories", "assets"],
    ["/app/company-calendar", "announcements"],
    ["/app/settings/company/ohs-risk-assessments", "occupationalSafety"],
  ])("maps %s to %s", (path, key) => {
    expect(moduleForPath(path)).toBe(key);
  });

  it.each(["/app", "/app/employees", "/app/settings/system/modules", "/app/support", "/app/leaves-report", "/app/settings/rules/visa"])(
    "treats core route %s as unowned",
    (path) => {
      expect(moduleForPath(path)).toBeNull();
    },
  );

  it("knows which keys are toggleable", () => {
    expect(isToggleableModule("leave")).toBe(true);
    expect(isToggleableModule("employee")).toBe(false);
  });
});
