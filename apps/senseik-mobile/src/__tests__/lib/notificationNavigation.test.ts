import { resolveNotificationNavigationTarget } from "@/lib/notificationNavigation";

describe("resolveNotificationNavigationTarget (N1)", () => {
  it("maps approval.task_assigned to the Approvals tab", () => {
    expect(resolveNotificationNavigationTarget("approval.task_assigned")).toBe("Approvals");
  });

  it("maps payroll.payslip_ready to the Payroll tab", () => {
    expect(resolveNotificationNavigationTarget("payroll.payslip_ready")).toBe("Payroll");
  });

  it("maps leave.added_on_behalf to the Leave tab", () => {
    expect(resolveNotificationNavigationTarget("leave.added_on_behalf")).toBe("Leave");
  });

  it("maps employee.birthday and announcement.published to Dashboard", () => {
    expect(resolveNotificationNavigationTarget("employee.birthday")).toBe("Dashboard");
    expect(resolveNotificationNavigationTarget("announcement.published")).toBe("Dashboard");
  });

  it("returns null for an unknown templateKey - never navigates on a guess", () => {
    expect(resolveNotificationNavigationTarget("something.unheard_of")).toBeNull();
  });

  it("returns null for ambiguous request.* keys (a request can be Leave, Advance, Expense, ...)", () => {
    expect(resolveNotificationNavigationTarget("request.created")).toBeNull();
    expect(resolveNotificationNavigationTarget("request.approved")).toBeNull();
    expect(resolveNotificationNavigationTarget("request.rejected")).toBeNull();
  });

  it("returns null for template keys with no mobile screen equivalent", () => {
    expect(resolveNotificationNavigationTarget("documents.expiry_reminder")).toBeNull();
    expect(resolveNotificationNavigationTarget("onboarding.task_assigned")).toBeNull();
    expect(resolveNotificationNavigationTarget("ohs.sgk_notification_due")).toBeNull();
  });
});
