import * as LocalAuthentication from "expo-local-authentication";
import { BiometricAvailable, BiometricType } from "@/types";
import { SecureStorage } from "./storage";

export class BiometricService {
  async isBiometricAvailable(): Promise<BiometricAvailable> {
    try {
      const compatible = await LocalAuthentication.hasHardwareAsync();
      const enrolled = await LocalAuthentication.isEnrolledAsync();
      const supportedTypes = await this.getSupportedBiometricTypes();

      return {
        available: compatible && enrolled,
        enrolled,
        supportedTypes
      };
    } catch (error) {
      console.error("Error checking biometric availability:", error);
      return {
        available: false,
        enrolled: false,
        supportedTypes: []
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
    } catch (error) {
      console.error("Error getting supported biometric types:", error);
      return [];
    }
  }

  async authenticate(reason: string = "Authenticate to access your account"): Promise<boolean> {
    try {
      const result = await LocalAuthentication.authenticateAsync({
        reason,
        fallbackLabel: "Use password instead",
        disableDeviceFallback: false
      });

      return result.success;
    } catch (error) {
      console.error("Biometric authentication failed:", error);
      return false;
    }
  }

  async enableBiometric(): Promise<void> {
    const available = await this.isBiometricAvailable();
    if (!available.available) {
      throw new Error("Biometric authentication is not available on this device");
    }

    // Authenticate first to enable
    const authenticated = await this.authenticate("Enable biometric login");
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
