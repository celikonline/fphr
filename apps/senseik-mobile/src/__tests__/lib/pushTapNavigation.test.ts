import type { NotificationResponse } from "expo-notifications";
import { navigateToTarget } from "@/lib/notificationNavigation";
import { appStackNavigation, resolvePushTapTarget } from "@/lib/pushTapNavigation";

function tapResponse(data: Record<string, unknown>): NotificationResponse {
  return {
    notification: { request: { identifier: "n-1", content: { data } } },
  } as unknown as NotificationResponse;
}

describe("resolvePushTapTarget", () => {
  it("resolves the backend payload's templateKey (data.type)", () => {
    expect(resolvePushTapTarget(tapResponse({ type: "payroll.payslip_ready", id: "x" }))).toEqual({
      stack: "Payroll",
    });
  });

  it("ignores a URL in the payload", () => {
    expect(resolvePushTapTarget(tapResponse({ url: "/app/payroll" }))).toBeNull();
    expect(
      resolvePushTapTarget(tapResponse({ type: "request.created", url: "senseik://payroll" }))
    ).toBeNull();
  });
});

describe("appStackNavigation", () => {
  it("forwards a stack target as a nested navigate under the root App route", () => {
    const root = { navigate: jest.fn() };
    navigateToTarget(appStackNavigation(root), { stack: "Approvals" });
    expect(root.navigate).toHaveBeenCalledWith("App", { screen: "Approvals" });
  });

  it("forwards a tab target through App > MainTabs", () => {
    const root = { navigate: jest.fn() };
    navigateToTarget(appStackNavigation(root), { tab: "CheckIn" });
    expect(root.navigate).toHaveBeenCalledWith("App", {
      screen: "MainTabs",
      params: { screen: "CheckIn" },
    });
  });
});
