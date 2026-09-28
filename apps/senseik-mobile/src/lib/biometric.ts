import * as LocalAuthentication from "expo-local-authentication";
import { BiometricAvailable, BiometricType } from "@/types";
import { SecureStorage } from "./storage";
import i18n from "./i18n";

export class BiometricService {
  async isBiometricAvailable(): Promise<BiometricAvailable> {
    try {
      const compatible = await LocalAuthentication.hasHardwareAsync();
      const enrolled = await LocalAuthentication.isEnrolledAsync();
      const supportedTypes = await this.getSupportedBiometricTypes();

      return {
        available: compatible && enrolled,
        enrolled,
        supportedTypes,
      };
    } catch {
      // Never log the raw error object (app-wide rule - see AuthService.ts).
      if (__DEV__) {
        console.error("Error checking biometric availability.");
      }
      return {
        available: false,
        enrolled: false,
        supportedTypes: [],
      };
    }
  }

  private async getSupportedBiometricTypes(): Promise<BiometricType[]> {
    try {
      const types = await LocalAuthentication.supportedAuthenticationTypesAsync();
      const supported: BiometricType[] = [];

      if (types.includes(LocalAuthentication.AuthenticationType.FINGERPRINT)) {
        supported.push(BiometricType.Fingerprint);
      }
      if (types.includes(LocalAuthentication.AuthenticationType.FACIAL_RECOGNITION)) {
        supported.push(BiometricType.FaceRecognition);
      }
      if (types.includes(LocalAuthentication.AuthenticationType.IRIS)) {
        supported.push(BiometricType.Iris);
      }

      return supported;
    } catch {
      // Never log the raw error object (app-wide rule - see AuthService.ts).
      if (__DEV__) {
        console.error("Error getting supported biometric types.");
      }
      return [];
    }
  }

  async authenticate(
    reason: string = i18n.t("common:biometric.authenticatePrompt")
  ): Promise<boolean> {
    try {
      // disableDeviceFallback: a stolen device with a known PIN must not bypass
      // biometrics for payroll/HR data. Users without biometrics log in with password.
      const result = await LocalAuthentication.authenticateAsync({
        promptMessage: reason,
        disableDeviceFallback: true,
      });

      return result.success;
    } catch {
      // Never log the raw error object (app-wide rule - see AuthService.ts).
      if (__DEV__) {
        console.error("Biometric authentication failed.");
      }
      return false;
    }
  }

  /**
   * FG-005 (Karar 3): confirms an action (a punch) with biometrics when the device has them enrolled;
   * a device without enrolled biometrics is not asked (resolves `true`). With biometrics, a cancelled
   * or failed prompt resolves `false`. Goes through `authenticate`, so `disableDeviceFallback` holds -
   * never falls back to the device PIN. Independent of the login's "biometric enabled" setting.
   */
  async confirmAction(reason: string): Promise<boolean> {
    const { available } = await this.isBiometricAvailable();
    if (!available) return true;
    return this.authenticate(reason);
  }

  async enableBiometric(): Promise<void> {
    const available = await this.isBiometricAvailable();
    if (!available.available) {
      throw new Error("Biometric authentication is not available on this device");
    }

    // Authenticate first to enable
    const authenticated = await this.authenticate(i18n.t("common:biometric.enablePrompt"));
    if (authenticated) {
      await SecureStorage.setBiometricEnabled(true);
    } else {
      throw new Error("Biometric authentication failed");
    }
  }

  async disableBiometric(): Promise<void> {
    await SecureStorage.setBiometricEnabled(false);
  }

  async isBiometricEnabled(): Promise<boolean> {
    return SecureStorage.getBiometricEnabled();
  }
}

export const biometricService = new BiometricService();
