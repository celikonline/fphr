import type { NavigationProp } from "@react-navigation/native";
import {
  navigateToTarget,
  resolveNotificationNavigationTarget,
} from "@/lib/notificationNavigation";
import type { AppStackParamList } from "@/types/navigation";

describe("resolveNotificationNavigationTarget (N1)", () => {
  it("maps approval.task_assigned to the Approvals stack screen", () => {
    expect(resolveNotificationNavigationTarget("approval.task_assigned")).toEqual({
      stack: "Approvals",
    });
  });

  it("maps payroll.payslip_ready to the Payroll stack screen", () => {
    expect(resolveNotificationNavigationTarget("payroll.payslip_ready")).toEqual({
      stack: "Payroll",
    });
  });

  it("maps leave.added_on_behalf to the Leave stack screen", () => {
    expect(resolveNotificationNavigationTarget("leave.added_on_behalf")).toEqual({
      stack: "Leave",
    });
  });

  it("maps employee.birthday and announcement.published to the Home tab", () => {
    expect(resolveNotificationNavigationTarget("employee.birthday")).toEqual({ tab: "Home" });
    expect(resolveNotificationNavigationTarget("announcement.published")).toEqual({ tab: "Home" });
  });

  it("returns null for an unknown templateKey - never navigates on a guess", () => {
    expect(resolveNotificationNavigationTarget("something.unheard_of")).toBeNull();
  });

  it("returns null for keys that only exist on Object.prototype", () => {
    expect(resolveNotificationNavigationTarget("constructor")).toBeNull();
    expect(resolveNotificationNavigationTarget("toString")).toBeNull();
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

describe("navigateToTarget", () => {
  function fakeNavigation() {
    const navigate = jest.fn();
    return {
      navigate,
      navigation: { navigate } as unknown as NavigationProp<AppStackParamList>,
    };
  }

  it("navigates straight to a stack screen", () => {
    const { navigate, navigation } = fakeNavigation();
    navigateToTarget(navigation, { stack: "Payroll" });
    expect(navigate).toHaveBeenCalledWith("Payroll");
  });

  it("navigates to a tab through MainTabs", () => {
    const { navigate, navigation } = fakeNavigation();
    navigateToTarget(navigation, { tab: "Home" });
    expect(navigate).toHaveBeenCalledWith("MainTabs", { screen: "Home" });
  });
});
