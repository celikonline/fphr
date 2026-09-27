import * as LocalAuthentication from "expo-local-authentication";
import { biometricService } from "@/lib/biometric";

describe("biometricService error logging", () => {
  it("never logs the raw error object - only a fixed-text dev log - when availability checks fail", async () => {
    const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
    const leakyError = Object.assign(new Error("hardware check failed"), {
      nativeStackAndroid: ["device-serial-12345"],
    });
    (LocalAuthentication.hasHardwareAsync as jest.Mock).mockRejectedValueOnce(leakyError);

    const result = await biometricService.isBiometricAvailable();

    expect(result).toEqual({ available: false, enrolled: false, supportedTypes: [] });
    expect(consoleError).toHaveBeenCalledWith("Error checking biometric availability.");
    for (const call of consoleError.mock.calls) {
      expect(call).not.toContain(leakyError);
      expect(JSON.stringify(call)).not.toMatch(/device-serial-/);
    }
    consoleError.mockRestore();
  });

  it("never logs the raw error object when authentication itself throws", async () => {
    const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
    const leakyError = new Error("authenticateAsync failed");
    (LocalAuthentication.authenticateAsync as jest.Mock).mockRejectedValueOnce(leakyError);

    const result = await biometricService.authenticate();

    expect(result).toBe(false);
    expect(consoleError).toHaveBeenCalledWith("Biometric authentication failed.");
    for (const call of consoleError.mock.calls) {
      expect(call).not.toContain(leakyError);
    }
    consoleError.mockRestore();
  });
});
