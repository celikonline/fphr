import { create } from "zustand";
import { User, AuthTokens, TenantChoice } from "@/types";
import { authService, isExpectedAuthFailure, LoginOutcome } from "@/services/AuthService";
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
  /** Non-null while the login endpoint returned `select_tenant` and the user has not picked one yet. */
  tenantChoices: TenantChoice[] | null;
  /**
   * Non-null while `login` returned `mfaRequired` and the user has not submitted a TOTP code yet.
   * In-memory only (this store has no persist middleware) - never written to SecureStore or logs.
   */
  mfaToken: string | null;
  /** The email from the `login` attempt that produced `mfaToken` - carried alongside it (same
   * lifetime, same in-memory-only rule) so `completeMfa` can record it as the last-login email for
   * biometric enrollment, exactly like `login`'s "ok" branch already does. */
  mfaEmail: string | null;
  biometricAvailable: boolean;
  biometricEnabled: boolean;

  // Actions
  /** `tenantSlug` re-submits a login after the caller picked one of `tenantChoices`. */
  login: (email: string, password: string, tenantSlug?: string) => Promise<LoginOutcome>;
  /** Submits the TOTP `code` for the pending `mfaToken` (set by `login`'s `mfaRequired` outcome). */
  completeMfa: (code: string) => Promise<void>;
  loginWithBiometric: (email: string) => Promise<void>;
  logout: () => Promise<void>;
  refreshToken: () => Promise<void>;
  checkAuth: () => Promise<void>;
  enableBiometric: () => Promise<void>;
  disableBiometric: () => Promise<void>;
  clearError: () => void;
  clearTenantChoices: () => void;
  /** Cancels a pending MFA challenge (user picked "Farklı hesapla giriş yap") - drops the `mfaToken`. */
  clearMfaChallenge: () => void;
}

export const useAuthStore = create<AuthStore>((set, get) => ({
  // Initial state
  isAuthenticated: false,
  isLoading: false,
  error: null,
  user: null,
  tenantId: null,
  tokens: null,
  tenantChoices: null,
  mfaToken: null,
  mfaEmail: null,
  biometricAvailable: false,
  biometricEnabled: false,

  // Actions
  login: async (email: string, password: string, tenantSlug?: string) => {
    set({ isLoading: true, error: null });
    try {
      const outcome = await authService.login(email, password, tenantSlug);
      if (outcome.status === "select_tenant") {
        set({ tenantChoices: outcome.tenants, mfaToken: null, mfaEmail: null, isLoading: false });
        return outcome;
      }
      if (outcome.status === "mfaRequired") {
        set({
          mfaToken: outcome.mfaToken,
          mfaEmail: email,
          tenantChoices: null,
          isLoading: false,
        });
        return outcome;
      }
      set({
        isAuthenticated: true,
        user: outcome.user,
        tenantId: outcome.user.tenantId,
        tokens: outcome.tokens,
        tenantChoices: null,
        mfaToken: null,
        mfaEmail: null,
        isLoading: false,
      });
      return outcome;
    } catch (error) {
      set({
        isAuthenticated: false,
        error: error instanceof Error ? error.message : "Login failed",
        isLoading: false,
      });
      throw error;
    }
  },

  completeMfa: async (code: string) => {
    const { mfaToken, mfaEmail } = get();
    if (!mfaToken) {
      const error = new Error("Doğrulama oturumu bulunamadı. Lütfen tekrar giriş yapın.");
      set({ error: error.message });
      throw error;
    }
    set({ isLoading: true, error: null });
    try {
      const outcome = await authService.completeMfa(mfaToken, code, mfaEmail);
      set({
        isAuthenticated: true,
        user: outcome.user,
        tenantId: outcome.user.tenantId,
        tokens: outcome.tokens,
        mfaToken: null,
        mfaEmail: null,
        isLoading: false,
      });
    } catch (error) {
      // The `mfaToken` is intentionally kept on failure (a wrong code should let the user retry the
      // same challenge) - callers clear it explicitly on `identity.mfa.token_expired`.
      set({
        isAuthenticated: false,
        error: error instanceof Error ? error.message : "MFA completion failed",
        isLoading: false,
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
        isLoading: false,
      });
    } catch (error) {
      set({
        isAuthenticated: false,
        error: error instanceof Error ? error.message : "Biometric login failed",
        isLoading: false,
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
        isLoading: false,
      });
    } catch (error) {
      set({
        error: error instanceof Error ? error.message : "Logout failed",
        isLoading: false,
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
        isLoading: false,
      });
    } catch (error) {
      set({
        isAuthenticated: false,
        error: error instanceof Error ? error.message : "Token refresh failed",
        isLoading: false,
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
          isLoading: false,
        });
      } else {
        set({ isAuthenticated: false, isLoading: false });
      }
    } catch (error) {
      // Never log the error object: a `/identity/me` axios failure carries the Authorization header.
      // A 401 (expired/invalid access token) is an expected session state, not an app bug - see
      // `isExpectedAuthFailure`.
      if (__DEV__ && !isExpectedAuthFailure(error)) {
        console.error("Auth check failed.");
      }
      set({
        isAuthenticated: false,
        error: error instanceof Error ? error.message : "Auth check failed",
        isLoading: false,
      });
    }
  },

  enableBiometric: async () => {
    set({ isLoading: true, error: null });
    try {
      await authService.enableBiometricLogin();
      set({
        biometricEnabled: true,
        isLoading: false,
      });
    } catch (error) {
      set({
        error: error instanceof Error ? error.message : "Failed to enable biometric",
        isLoading: false,
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
        isLoading: false,
      });
    } catch (error) {
      set({
        error: error instanceof Error ? error.message : "Failed to disable biometric",
        isLoading: false,
      });
      throw error;
    }
  },

  clearError: () => set({ error: null }),

  clearTenantChoices: () => set({ tenantChoices: null }),

  clearMfaChallenge: () => set({ mfaToken: null, mfaEmail: null, error: null }),
}));

// apiClient has no direct dependency on the store (would be circular: apiClient -> authStore ->
// AuthService -> apiClient); it calls this callback instead when a token refresh fails, so an
// already-cleared session on the server can't keep showing the app as signed in.
setUnauthorizedHandler(() => {
  useAuthStore.setState({ isAuthenticated: false, tokens: null, user: null, tenantId: null });
});
