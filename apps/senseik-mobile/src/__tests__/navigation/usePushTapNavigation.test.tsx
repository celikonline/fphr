import React from "react";
import { act, create, type ReactTestRenderer } from "react-test-renderer";
import * as Notifications from "expo-notifications";
import { usePushTapNavigation } from "@/navigation/usePushTapNavigation";

const getLastResponse = Notifications.getLastNotificationResponse as jest.Mock;
const addResponseListener = Notifications.addNotificationResponseReceivedListener as jest.Mock;

function tapResponse(identifier: string, type: string): Notifications.NotificationResponse {
  return {
    notification: { request: { identifier, content: { data: { type, id: "x" } } } },
  } as unknown as Notifications.NotificationResponse;
}

const root = { navigate: jest.fn() };

function Harness({ canNavigate }: { canNavigate: boolean }) {
  usePushTapNavigation(root, canNavigate);
  return null;
}

let renderer: ReactTestRenderer | undefined;

async function render(canNavigate: boolean) {
  await act(async () => {
    renderer = create(<Harness canNavigate={canNavigate} />);
  });
}

async function rerender(canNavigate: boolean) {
  await act(async () => {
    renderer?.update(<Harness canNavigate={canNavigate} />);
  });
}

/** Delivers a warm tap through the listener the hook registered. */
async function tap(response: Notifications.NotificationResponse) {
  const listener = addResponseListener.mock.calls[0][0] as (
    r: Notifications.NotificationResponse
  ) => void;
  await act(async () => listener(response));
}

describe("usePushTapNavigation", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    getLastResponse.mockReturnValue(null);
  });

  afterEach(async () => {
    await act(async () => renderer?.unmount());
    renderer = undefined;
  });

  it("holds a cold-start tap until the user is signed in and navigation is ready", async () => {
    getLastResponse.mockReturnValue(tapResponse("n-1", "approval.task_assigned"));

    await render(false);
    expect(root.navigate).not.toHaveBeenCalled();

    await rerender(true);
    expect(root.navigate).toHaveBeenCalledTimes(1);
    expect(root.navigate).toHaveBeenCalledWith("App", { screen: "Approvals" });
    expect(Notifications.clearLastNotificationResponse).toHaveBeenCalled();
  });

  it("navigates right away on a warm tap once navigation is possible", async () => {
    await render(true);
    await tap(tapResponse("n-2", "announcement.published"));

    expect(root.navigate).toHaveBeenCalledWith("App", {
      screen: "MainTabs",
      params: { screen: "Home" },
    });
  });

  it("acts once when the launching tap arrives both as the last response and via the listener", async () => {
    const response = tapResponse("n-3", "payroll.payslip_ready");
    getLastResponse.mockReturnValue(response);

    await render(true);
    await tap(response);

    expect(root.navigate).toHaveBeenCalledTimes(1);
  });

  it("does not navigate for a templateKey with no mobile screen", async () => {
    await render(true);
    await tap(tapResponse("n-4", "request.created"));

    expect(root.navigate).not.toHaveBeenCalled();
  });

  it("removes the response listener on unmount", async () => {
    const remove = jest.fn();
    addResponseListener.mockReturnValueOnce({ remove });

    await render(true);
    await act(async () => renderer?.unmount());
    renderer = undefined;

    expect(remove).toHaveBeenCalled();
  });
});
