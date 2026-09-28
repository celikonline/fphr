import AsyncStorage from "@react-native-async-storage/async-storage";
import * as SecureStore from "expo-secure-store";

/**
 * Release builds must not write tokens, PII or error payloads to logcat.
 * All storage diagnostics go through this dev-only helper.
 */
function logError(...args: unknown[]): void {
  if (__DEV__) {
    console.error(...args);
  }
}

/**
 * iOS keychain items stay on this device only (no iCloud/backup extraction).
 * Android uses the default encrypted SharedPreferences backend.
 */
const TOKEN_STORE_OPTIONS: SecureStore.SecureStoreOptions = {
  keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY,
};

// Secure Storage for sensitive data (tokens)
export const SecureStorage = {
  async setAccessToken(token: string): Promise<void> {
    try {
      await SecureStore.setItemAsync("accessToken", token, TOKEN_STORE_OPTIONS);
    } catch (error) {
      logError("Failed to store access token:", error);
      throw error;
    }
  },

  async getAccessToken(): Promise<string | null> {
    try {
      return await SecureStore.getItemAsync("accessToken");
    } catch (error) {
      logError("Failed to retrieve access token:", error);
      return null;
    }
  },

  async setRefreshToken(token: string): Promise<void> {
    try {
      await SecureStore.setItemAsync("refreshToken", token, TOKEN_STORE_OPTIONS);
    } catch (error) {
      logError("Failed to store refresh token:", error);
      throw error;
    }
  },

  async getRefreshToken(): Promise<string | null> {
    try {
      return await SecureStore.getItemAsync("refreshToken");
    } catch (error) {
      logError("Failed to retrieve refresh token:", error);
      return null;
    }
  },

  async setTenantId(tenantId: string): Promise<void> {
    try {
      await SecureStore.setItemAsync("tenantId", tenantId, TOKEN_STORE_OPTIONS);
    } catch (error) {
      logError("Failed to store tenant ID:", error);
      throw error;
    }
  },

  async getTenantId(): Promise<string | null> {
    try {
      return await SecureStore.getItemAsync("tenantId");
    } catch (error) {
      logError("Failed to retrieve tenant ID:", error);
      return null;
    }
  },

  async setUserId(userId: string): Promise<void> {
    try {
      await SecureStore.setItemAsync("userId", userId, TOKEN_STORE_OPTIONS);
    } catch (error) {
      logError("Failed to store user ID:", error);
      throw error;
    }
  },

  async getUserId(): Promise<string | null> {
    try {
      return await SecureStore.getItemAsync("userId");
    } catch (error) {
      logError("Failed to retrieve user ID:", error);
      return null;
    }
  },

  async setBiometricEnabled(enabled: boolean): Promise<void> {
    try {
      await SecureStore.setItemAsync("biometricEnabled", String(enabled), TOKEN_STORE_OPTIONS);
    } catch (error) {
      logError("Failed to store biometric enabled state:", error);
      throw error;
    }
  },

  async getBiometricEnabled(): Promise<boolean> {
    try {
      const value = await SecureStore.getItemAsync("biometricEnabled");
      return value === "true";
    } catch (error) {
      logError("Failed to retrieve biometric enabled state:", error);
      return false;
    }
  },

  async clearAll(): Promise<void> {
    try {
      await SecureStore.deleteItemAsync("accessToken");
      await SecureStore.deleteItemAsync("refreshToken");
      await SecureStore.deleteItemAsync("tenantId");
      await SecureStore.deleteItemAsync("userId");
      await SecureStore.deleteItemAsync("biometricEnabled");
    } catch (error) {
      logError("Failed to clear secure storage:", error);
      throw error;
    }
  },
};

// Async Storage for non-sensitive data (preferences)
export const PreferenceStorage = {
  async setTheme(theme: "light" | "dark" | "auto"): Promise<void> {
    try {
      await AsyncStorage.setItem("theme", theme);
    } catch (error) {
      logError("Failed to store theme preference:", error);
    }
  },

  async getTheme(): Promise<"light" | "dark" | "auto"> {
    try {
      const theme = await AsyncStorage.getItem("theme");
      return (theme as "light" | "dark" | "auto" | null) || "auto";
    } catch (error) {
      logError("Failed to retrieve theme preference:", error);
      return "auto";
    }
  },

  async setLanguage(language: string): Promise<void> {
    try {
      await AsyncStorage.setItem("language", language);
    } catch (error) {
      logError("Failed to store language preference:", error);
    }
  },

  async getLanguage(): Promise<string> {
    try {
      const language = await AsyncStorage.getItem("language");
      return language || "tr";
    } catch (error) {
      logError("Failed to retrieve language preference:", error);
      return "tr";
    }
  },

  async setLastLoginEmail(email: string): Promise<void> {
    try {
      await AsyncStorage.setItem("lastLoginEmail", email);
    } catch (error) {
      logError("Failed to store last login email:", error);
    }
  },

  async getLastLoginEmail(): Promise<string | null> {
    try {
      return await AsyncStorage.getItem("lastLoginEmail");
    } catch (error) {
      logError("Failed to retrieve last login email:", error);
      return null;
    }
  },

  async setAppState(state: unknown): Promise<void> {
    try {
      await AsyncStorage.setItem("appState", JSON.stringify(state));
    } catch (error) {
      logError("Failed to store app state:", error);
    }
  },

  async getAppState(): Promise<unknown | null> {
    try {
      const state = await AsyncStorage.getItem("appState");
      return state ? JSON.parse(state) : null;
    } catch (error) {
      logError("Failed to retrieve app state:", error);
      return null;
    }
  },

  async clearAll(): Promise<void> {
    try {
      await AsyncStorage.removeItem("theme");
      await AsyncStorage.removeItem("language");
      await AsyncStorage.removeItem("lastLoginEmail");
      await AsyncStorage.removeItem("appState");
    } catch (error) {
      logError("Failed to clear preference storage:", error);
    }
  },
};
