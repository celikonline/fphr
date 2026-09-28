import { Platform } from "react-native";
import * as Notifications from "expo-notifications";
import { pushNotificationService } from "@/services/PushNotificationService";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");

// NOTE: `pushNotificationService` is a module-level singleton with an in-memory
// `registeredToken` field (see PushNotificationService.ts) that has no test-only reset hook -
// this file relies on declaration order for the one assertion that needs a pristine "never
// registered" state (`unregisterPushToken` describe block below), so it runs before any other
// test in this file calls `registerPushToken`. Keep it first if this file is reordered.
describe("PushNotificationService.unregisterPushToken", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it("does nothing when no token was ever registered", async () => {
    await pushNotificationService.unregisterPushToken();

    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it("revokes the last registered token", async () => {
    process.env.EXPO_PUBLIC_PROJECT_ID = "test-project-id";
    (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });
    await pushNotificationService.registerPushToken();
    (apiClient.post as jest.Mock).mockClear();

    await pushNotificationService.unregisterPushToken();

    expect(apiClient.post).toHaveBeenCalledWith("/api/v1/notifications/my/devices/revoke", {
      token: "fake-token",
    });
  });

  it("never throws when the revoke request fails (best-effort, must never block logout)", async () => {
    process.env.EXPO_PUBLIC_PROJECT_ID = "test-project-id";
    (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });
    await pushNotificationService.registerPushToken();
    (apiClient.post as jest.Mock).mockRejectedValue(new Error("network"));

    await expect(pushNotificationService.unregisterPushToken()).resolves.toBeUndefined();
  });

  it("logs nothing when the revoke request fails", async () => {
    process.env.EXPO_PUBLIC_PROJECT_ID = "test-project-id";
    (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });
    await pushNotificationService.registerPushToken();
    (apiClient.post as jest.Mock).mockRejectedValue({
      isAxiosError: true,
      response: { status: 500 },
    });
    const warnSpy = jest.spyOn(console, "warn").mockImplementation(() => undefined);
    const errorSpy = jest.spyOn(console, "error").mockImplementation(() => undefined);

    await pushNotificationService.unregisterPushToken();

    expect(warnSpy).not.toHaveBeenCalled();
    expect(errorSpy).not.toHaveBeenCalled();
  });
});

describe("PushNotificationService.registerPushToken", () => {
  const originalProjectId = process.env.EXPO_PUBLIC_PROJECT_ID;

  beforeEach(() => {
    jest.clearAllMocks();
  });

  afterEach(() => {
    process.env.EXPO_PUBLIC_PROJECT_ID = originalProjectId;
    jest.restoreAllMocks();
  });

  it("skips registration and resolves to null when EXPO_PUBLIC_PROJECT_ID is not configured (B1: no red LogBox toast)", async () => {
    delete process.env.EXPO_PUBLIC_PROJECT_ID;

    const result = await pushNotificationService.registerPushToken();

    expect(result).toBeNull();
    expect(Notifications.getExpoPushTokenAsync).not.toHaveBeenCalled();
    expect(apiClient.post).not.toHaveBeenCalled();
  });

  it("logs nothing when EXPO_PUBLIC_PROJECT_ID is not configured (B7: console.warn also renders an on-screen LogBox banner)", async () => {
    delete process.env.EXPO_PUBLIC_PROJECT_ID;
    const warnSpy = jest.spyOn(console, "warn").mockImplementation(() => undefined);
    const errorSpy = jest.spyOn(console, "error").mockImplementation(() => undefined);

    await pushNotificationService.registerPushToken();

    expect(warnSpy).not.toHaveBeenCalled();
    expect(errorSpy).not.toHaveBeenCalled();
  });

  it("logs nothing when the backend device-registration endpoint fails (B7)", async () => {
    process.env.EXPO_PUBLIC_PROJECT_ID = "test-project-id";
    (apiClient.post as jest.Mock).mockRejectedValue({
      isAxiosError: true,
      response: { status: 400, data: { code: "notifications.device.invalid_token" } },
    });
    const warnSpy = jest.spyOn(console, "warn").mockImplementation(() => undefined);
    const errorSpy = jest.spyOn(console, "error").mockImplementation(() => undefined);

    await pushNotificationService.registerPushToken();

    expect(warnSpy).not.toHaveBeenCalled();
    expect(errorSpy).not.toHaveBeenCalled();
  });

  it("registers the device with the token and platform - no userId in the request", async () => {
    process.env.EXPO_PUBLIC_PROJECT_ID = "test-project-id";
    (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });

    const result = await pushNotificationService.registerPushToken();

    expect(result).toBe("fake-token");
    expect(Notifications.getExpoPushTokenAsync).toHaveBeenCalledWith({
      projectId: "test-project-id",
    });
    expect(apiClient.post).toHaveBeenCalledWith("/api/v1/notifications/my/devices", {
      token: "fake-token",
      platform: Platform.OS,
    });
  });

  it("does not throw when the backend device-registration endpoint fails (e.g. network error)", async () => {
    process.env.EXPO_PUBLIC_PROJECT_ID = "test-project-id";
    (apiClient.post as jest.Mock).mockRejectedValue(new Error("network"));

    await expect(pushNotificationService.registerPushToken()).resolves.toBe("fake-token");
  });
});
