import { create } from "zustand";
import { User, AuthTokens } from "@/types";
import { authService } from "@/services/AuthService";
import { SecureStorage } from "@/lib/storage";
import { setUnauthorizedHandler } from "@/lib/apiClient";

export interface AuthStore {
  // State
  isAuthenticated: boolean;
  isLoading: boolean;
  error: string | null;
  user: User | null;
  tenantId: string | null;
  tokens: AuthTokens | null;
  biometricAvailable: boolean;
  biometricEnabled: boolean;

  // Actions
  login: (email: string, password: string) => Promise<void>;
  loginWithBiometric: (email: string) => Promise<void>;
  logout: () => Promise<void>;
  refreshToken: () => Promise<void>;
  checkAuth: () => Promise<void>;
  enableBiometric: () => Promise<void>;
  disableBiometric: () => Promise<void>;
  clearError: () => void;
}

export const useAuthStore = create<AuthStore>((set) => ({
  // Initial state
  isAuthenticated: false,
  isLoading: false,
  error: null,
  user: null,
  tenantId: null,
  tokens: null,
  biometricAvailable: false,
  biometricEnabled: false,

  // Actions
  login: async (email: string, password: string) => {
    set({ isLoading: true, error: null });
    try {
      const response = await authService.login(email, password);
      set({
        isAuthenticated: true,
        user: response.user,
        tenantId: response.tenantId,
        tokens: response.tokens,
        isLoading: false
      });
    } catch (error) {
      set({
        isAuthenticated: false,
        error: error instanceof Error ? error.message : "Login failed",
        isLoading: false
      });
      throw error;
    }
  },

  loginWithBiometric: async (email: string) => {
    set({ isLoading: true, error: null });
    try {
      const response = await authService.loginWithBiometric(email);
      set({
        isAuthenticated: true,
        user: response.user,
        tenantId: response.tenantId,
        tokens: response.tokens,
        isLoading: false
      });
    } catch (error) {
      set({
        isAuthenticated: false,
        error: error instanceof Error ? error.message : "Biometric login failed",
        isLoading: false
      });
      throw error;
    }
  },

  logout: async () => {
    set({ isLoading: true, error: null });
    try {
      await authService.logout();
      set({
        isAuthenticated: false,
        user: null,
        tenantId: null,
        tokens: null,
        isLoading: false
      });
    } catch (error) {
      set({
        error: error instanceof Error ? error.message : "Logout failed",
        isLoading: false
      });
      throw error;
    }
  },

  refreshToken: async () => {
    set({ isLoading: true, error: null });
    try {
      const tokens = await authService.refreshToken();
      set({
        tokens,
        isLoading: false
      });
    } catch (error) {
      set({
        isAuthenticated: false,
        error: error instanceof Error ? error.message : "Token refresh failed",
        isLoading: false
      });
      throw error;
    }
  },

  checkAuth: async () => {
    set({ isLoading: true, error: null });
    try {
      const accessToken = await SecureStorage.getAccessToken();
      if (!accessToken) {
        set({ isAuthenticated: false, isLoading: false });
        return;
      }

      const isValid = await authService.validateToken();
      if (isValid) {
        const user = await authService.getCurrentUser();
        const tenantId = await SecureStorage.getTenantId();
        const biometricEnabled = await SecureStorage.getBiometricEnabled();

        set({
          isAuthenticated: true,
          user: user || undefined,
          tenantId: tenantId || undefined,
          biometricEnabled,
          isLoading: false
        });
      } else {
        set({ isAuthenticated: false, isLoading: false });
      }
    } catch (error) {
      console.error("Auth check failed:", error);
      set({
        isAuthenticated: false,
        error: error instanceof Error ? error.message : "Auth check failed",
        isLoading: false
      });
    }
  },

  enableBiometric: async () => {
    set({ isLoading: true, error: null });
    try {
      await authService.enableBiometricLogin();
      set({
        biometricEnabled: true,
        isLoading: false
      });
    } catch (error) {
      set({
        error: error instanceof Error ? error.message : "Failed to enable biometric",
        isLoading: false
      });
      throw error;
    }
  },

  disableBiometric: async () => {
    set({ isLoading: true, error: null });
    try {
      await SecureStorage.setBiometricEnabled(false);
      set({
        biometricEnabled: false,
        isLoading: false
      });
    } catch (error) {
      set({
        error: error instanceof Error ? error.message : "Failed to disable biometric",
        isLoading: false
      });
      throw error;
    }
  },

  clearError: () => set({ error: null })
}));

// apiClient has no direct dependency on the store (would be circular: apiClient -> authStore ->
// AuthService -> apiClient); it calls this callback instead when a token refresh fails, so an
// already-cleared session on the server can't keep showing the app as signed in.
setUnauthorizedHandler(() => {
  useAuthStore.setState({ isAuthenticated: false, tokens: null, user: null, tenantId: null });
});
