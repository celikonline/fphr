import * as SecureStore from "expo-secure-store";
import AsyncStorage from "@react-native-async-storage/async-storage";
import { PreferenceStorage, SecureStorage } from "@/lib/storage";

describe("storage error logging (QA-004)", () => {
  it("logs once via console.error in dev and does not throw RangeError (logError no longer recurses)", async () => {
    const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
    const boom = new Error("boom");
    (SecureStore.getItemAsync as jest.Mock).mockRejectedValueOnce(boom);

    await expect(SecureStorage.getAccessToken()).resolves.toBeNull();

    expect(consoleError).toHaveBeenCalledTimes(1);
    expect(consoleError).toHaveBeenCalledWith("Failed to retrieve access token:", boom);
    consoleError.mockRestore();
  });

  it("get* functions fall back to null/default instead of throwing when SecureStore rejects", async () => {
    jest.spyOn(console, "error").mockImplementation(() => undefined);
    (SecureStore.getItemAsync as jest.Mock).mockRejectedValueOnce(new Error("refresh fail"));
    await expect(SecureStorage.getRefreshToken()).resolves.toBeNull();

    (SecureStore.getItemAsync as jest.Mock).mockRejectedValueOnce(new Error("tenant fail"));
    await expect(SecureStorage.getTenantId()).resolves.toBeNull();

    (SecureStore.getItemAsync as jest.Mock).mockRejectedValueOnce(new Error("user fail"));
    await expect(SecureStorage.getUserId()).resolves.toBeNull();

    (SecureStore.getItemAsync as jest.Mock).mockRejectedValueOnce(new Error("biometric fail"));
    await expect(SecureStorage.getBiometricEnabled()).resolves.toBe(false);
    (jest.spyOn(console, "error") as jest.SpyInstance).mockRestore();
  });

  it("set* functions still rethrow when SecureStore rejects", async () => {
    jest.spyOn(console, "error").mockImplementation(() => undefined);
    const failure = new Error("store fail");
    (SecureStore.setItemAsync as jest.Mock).mockRejectedValueOnce(failure);

    await expect(SecureStorage.setAccessToken("token-value")).rejects.toThrow(failure);
    (jest.spyOn(console, "error") as jest.SpyInstance).mockRestore();
  });

  it("clearAll rethrows on SecureStore failure", async () => {
    jest.spyOn(console, "error").mockImplementation(() => undefined);
    const failure = new Error("clear fail");
    (SecureStore.deleteItemAsync as jest.Mock).mockRejectedValueOnce(failure);

    await expect(SecureStorage.clearAll()).rejects.toThrow(failure);
    (jest.spyOn(console, "error") as jest.SpyInstance).mockRestore();
  });

  it("PreferenceStorage get* falls back to defaults instead of throwing when AsyncStorage rejects", async () => {
    jest.spyOn(console, "error").mockImplementation(() => undefined);
    (AsyncStorage.getItem as jest.Mock).mockRejectedValueOnce(new Error("theme fail"));
    await expect(PreferenceStorage.getTheme()).resolves.toBe("auto");

    (AsyncStorage.getItem as jest.Mock).mockRejectedValueOnce(new Error("language fail"));
    await expect(PreferenceStorage.getLanguage()).resolves.toBe("tr");
    (jest.spyOn(console, "error") as jest.SpyInstance).mockRestore();
  });

  it("PreferenceStorage set* swallows AsyncStorage errors (does not throw)", async () => {
    const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
    const failure = new Error("save fail");
    (AsyncStorage.setItem as jest.Mock).mockRejectedValueOnce(failure);

    await expect(PreferenceStorage.setTheme("dark")).resolves.toBeUndefined();
    expect(consoleError).toHaveBeenCalledWith("Failed to store theme preference:", failure);
    consoleError.mockRestore();
  });
});
