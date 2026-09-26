import * as Notifications from "expo-notifications";
import { pushNotificationService } from "@/services/PushNotificationService";
import { apiClient } from "@/lib/apiClient";
import { SecureStorage } from "@/lib/storage";

jest.mock("@/lib/apiClient");
jest.mock("@/lib/storage");

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

  it("logs nothing when the backend push-token endpoint fails (B7)", async () => {
    process.env.EXPO_PUBLIC_PROJECT_ID = "test-project-id";
    (SecureStorage.getUserId as jest.Mock).mockResolvedValue("user-1");
    (apiClient.post as jest.Mock).mockRejectedValue({
      isAxiosError: true,
      response: { status: 404 },
    });
    const warnSpy = jest.spyOn(console, "warn").mockImplementation(() => undefined);
    const errorSpy = jest.spyOn(console, "error").mockImplementation(() => undefined);

    await pushNotificationService.registerPushToken();

    expect(warnSpy).not.toHaveBeenCalled();
    expect(errorSpy).not.toHaveBeenCalled();
  });

  it("fetches and uploads the push token when a project id is configured", async () => {
    process.env.EXPO_PUBLIC_PROJECT_ID = "test-project-id";
    (SecureStorage.getUserId as jest.Mock).mockResolvedValue("user-1");
    (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });

    const result = await pushNotificationService.registerPushToken();

    expect(result).toBe("fake-token");
    expect(Notifications.getExpoPushTokenAsync).toHaveBeenCalledWith({
      projectId: "test-project-id",
    });
    expect(apiClient.post).toHaveBeenCalledWith("/api/users/user-1/push-tokens", {
      token: "fake-token",
      type: "expo",
    });
  });

  it("does not throw when the backend push-token endpoint doesn't exist yet (B1: no backend endpoint)", async () => {
    process.env.EXPO_PUBLIC_PROJECT_ID = "test-project-id";
    (SecureStorage.getUserId as jest.Mock).mockResolvedValue("user-1");
    (apiClient.post as jest.Mock).mockRejectedValue({
      isAxiosError: true,
      response: { status: 404 },
    });

    await expect(pushNotificationService.registerPushToken()).resolves.toBe("fake-token");
  });
});
