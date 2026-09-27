import { resolvePushNavigationScreen } from "@/lib/pushNavigation";

describe("resolvePushNavigationScreen", () => {
  it("returns the screen when data.screen names a known tab", () => {
    expect(resolvePushNavigationScreen({ screen: "Leave" })).toBe("Leave");
  });

  it("ignores data.screen when it names an unknown route", () => {
    expect(resolvePushNavigationScreen({ screen: "Admin" })).toBeNull();
  });

  it("maps a birthday notification type to Dashboard", () => {
    expect(resolvePushNavigationScreen({ type: "employee.birthday" })).toBe("Dashboard");
  });

  it("maps a work anniversary notification type to Dashboard", () => {
    expect(resolvePushNavigationScreen({ type: "employee.anniversary" })).toBe("Dashboard");
  });

  it("maps an announcement notification type to Dashboard", () => {
    expect(resolvePushNavigationScreen({ type: "announcement.published" })).toBe("Dashboard");
  });

  it("maps approval.task_assigned to Approvals (aligned with notificationNavigation.ts)", () => {
    expect(resolvePushNavigationScreen({ type: "approval.task_assigned" })).toBe("Approvals");
  });

  it("maps payroll.payslip_ready to Payroll (aligned with notificationNavigation.ts)", () => {
    expect(resolvePushNavigationScreen({ type: "payroll.payslip_ready" })).toBe("Payroll");
  });

  it("maps leave.added_on_behalf to Leave (aligned with notificationNavigation.ts)", () => {
    expect(resolvePushNavigationScreen({ type: "leave.added_on_behalf" })).toBe("Leave");
  });

  it("prefers an explicit data.screen over data.type when both are present", () => {
    expect(resolvePushNavigationScreen({ screen: "Payroll", type: "employee.birthday" })).toBe(
      "Payroll"
    );
  });

  it("returns null for an unknown notification type", () => {
    expect(resolvePushNavigationScreen({ type: "something.unrelated" })).toBeNull();
  });

  it("returns null when data is undefined", () => {
    expect(resolvePushNavigationScreen(undefined)).toBeNull();
  });

  it("returns null for an empty data payload", () => {
    expect(resolvePushNavigationScreen({})).toBeNull();
  });

  it("existing approval/request routing is unaffected (data.screen still passes through)", () => {
    expect(resolvePushNavigationScreen({ screen: "Approvals" })).toBe("Approvals");
  });
});
