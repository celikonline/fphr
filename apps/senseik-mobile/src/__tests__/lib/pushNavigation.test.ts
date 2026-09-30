import { resolvePushNavigationScreen } from "@/lib/pushNavigation";

describe("resolvePushNavigationScreen", () => {
  it("returns the stack screen when data.screen names a known screen", () => {
    expect(resolvePushNavigationScreen({ screen: "Leave" })).toEqual({ stack: "Leave" });
  });

  it("maps the legacy Dashboard tab name in data.screen to the Home tab", () => {
    expect(resolvePushNavigationScreen({ screen: "Dashboard" })).toEqual({ tab: "Home" });
  });

  it("ignores data.screen when it names an unknown route", () => {
    expect(resolvePushNavigationScreen({ screen: "Admin" })).toBeNull();
  });

  it("ignores data.screen/data.type that only exist on Object.prototype", () => {
    expect(resolvePushNavigationScreen({ screen: "constructor" })).toBeNull();
    expect(resolvePushNavigationScreen({ type: "toString" })).toBeNull();
  });

  it("maps a birthday notification type to the Home tab", () => {
    expect(resolvePushNavigationScreen({ type: "employee.birthday" })).toEqual({ tab: "Home" });
  });

  it("maps a work anniversary notification type to the Home tab", () => {
    expect(resolvePushNavigationScreen({ type: "employee.anniversary" })).toEqual({ tab: "Home" });
  });

  it("maps an announcement notification type to the Home tab", () => {
    expect(resolvePushNavigationScreen({ type: "announcement.published" })).toEqual({
      tab: "Home",
    });
  });

  it("maps approval.task_assigned to Approvals (aligned with notificationNavigation.ts)", () => {
    expect(resolvePushNavigationScreen({ type: "approval.task_assigned" })).toEqual({
      stack: "Approvals",
    });
  });

  it("maps payroll.payslip_ready to Payroll (aligned with notificationNavigation.ts)", () => {
    expect(resolvePushNavigationScreen({ type: "payroll.payslip_ready" })).toEqual({
      stack: "Payroll",
    });
  });

  it("maps leave.added_on_behalf to Leave (aligned with notificationNavigation.ts)", () => {
    expect(resolvePushNavigationScreen({ type: "leave.added_on_behalf" })).toEqual({
      stack: "Leave",
    });
  });

  it("prefers an explicit data.screen over data.type when both are present", () => {
    expect(resolvePushNavigationScreen({ screen: "Payroll", type: "employee.birthday" })).toEqual({
      stack: "Payroll",
    });
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
    expect(resolvePushNavigationScreen({ screen: "Approvals" })).toEqual({ stack: "Approvals" });
  });
});
