import { authService } from "@/services/AuthService";
import { apiClient } from "@/lib/apiClient";
import { SecureStorage, PreferenceStorage } from "@/lib/storage";
import { biometricService } from "@/lib/biometric";
import { pushNotificationService } from "@/services/PushNotificationService";

jest.mock("@/lib/apiClient");
jest.mock("@/lib/storage");
jest.mock("@/lib/biometric");
jest.mock("@/services/PushNotificationService");

// Mirrors MeDto (Sense.Modules.Identity.Application/Dtos.cs).
const meResponse = {
  userId: "user-123",
  email: "test@example.com",
  firstName: "John",
  lastName: "Doe",
  employeeId: null,
  preferredLanguage: "tr",
  mfaEnabled: false,
  tenant: { id: "tenant-123", name: "Demo Co", slug: "demo-hr-company" },
  roles: [{ id: "role-1", code: "employee", name: "Çalışan" }],
  permissions: ["leave.view"],
};

// Mirrors AuthTokensDto.
const tokensResponse = {
  accessToken: "access-token",
  accessTokenExpiresAt: "2026-09-25T12:00:00Z",
  refreshToken: "refresh-token",
  refreshTokenExpiresAt: "2026-10-02T12:00:00Z",
  tokenType: "Bearer",
};

describe("AuthService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("login", () => {
    it("stores tokens and maps /me on status ok", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({
        data: { status: "ok", tokens: tokensResponse },
      });
      (apiClient.get as jest.Mock).mockResolvedValue({ data: meResponse });

      const outcome = await authService.login("test@example.com", "password123");

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/identity/auth/login", {
        email: "test@example.com",
        password: "password123",
        tenantSlug: undefined,
      });
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/identity/me");
      expect(outcome).toEqual({
        status: "ok",
        user: {
          id: "user-123",
          tenantId: "tenant-123",
          email: "test@example.com",
          firstName: "John",
          lastName: "Doe",
          employeeId: null,
          preferredLanguage: "tr",
          mfaEnabled: false,
          roles: ["employee"],
          permissions: ["leave.view"],
          tenant: meResponse.tenant,
        },
        tokens: tokensResponse,
      });
      expect(SecureStorage.setAccessToken).toHaveBeenCalledWith("access-token");
      expect(SecureStorage.setRefreshToken).toHaveBeenCalledWith("refresh-token");
      expect(SecureStorage.setUserId).toHaveBeenCalledWith("user-123");
      expect(SecureStorage.setTenantId).toHaveBeenCalledWith("tenant-123");
      expect(PreferenceStorage.setLastLoginEmail).toHaveBeenCalledWith("test@example.com");
    });

    it("returns tenant choices without storing tokens on status select_tenant", async () => {
      const tenants = [
        { tenantId: "tenant-123", slug: "demo-hr-company", name: "Demo Co" },
        { tenantId: "tenant-456", slug: "biriksin", name: "Biriksin" },
      ];
      (apiClient.post as jest.Mock).mockResolvedValue({
        data: { status: "select_tenant", tenants },
      });

      const outcome = await authService.login("owner@demo.local", "Demo123!");

      expect(outcome).toEqual({ status: "select_tenant", tenants });
      expect(apiClient.get).not.toHaveBeenCalled();
      expect(SecureStorage.setAccessToken).not.toHaveBeenCalled();
      expect(SecureStorage.setRefreshToken).not.toHaveBeenCalled();
      expect(SecureStorage.setUserId).not.toHaveBeenCalled();
      expect(SecureStorage.setTenantId).not.toHaveBeenCalled();
    });

    it("re-submits the chosen tenantSlug and logs in", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({
        data: { status: "ok", tokens: tokensResponse },
      });
      (apiClient.get as jest.Mock).mockResolvedValue({ data: meResponse });

      const outcome = await authService.login("owner@demo.local", "Demo123!", "biriksin");

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/identity/auth/login", {
        email: "owner@demo.local",
        password: "Demo123!",
        tenantSlug: "biriksin",
      });
      expect(outcome.status).toBe("ok");
    });

    it("returns the mfaRequired outcome with the challenge token, without storing tokens", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({
        data: { status: "mfa_required", mfaToken: "mfa-token-abc" },
      });

      const outcome = await authService.login("test@example.com", "password123");

      expect(outcome).toEqual({ status: "mfaRequired", mfaToken: "mfa-token-abc" });
      expect(apiClient.get).not.toHaveBeenCalled();
      expect(SecureStorage.setAccessToken).not.toHaveBeenCalled();
      expect(SecureStorage.setRefreshToken).not.toHaveBeenCalled();
    });

    it("throws on status mfa_required without a mfaToken", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({
        data: { status: "mfa_required" },
      });

      await expect(authService.login("test@example.com", "password123")).rejects.toThrow(
        "Beklenmeyen giriş yanıtı."
      );
      expect(SecureStorage.setAccessToken).not.toHaveBeenCalled();
    });

    it("throws without storing tokens on invalid credentials (401)", async () => {
      const unauthorizedError = Object.assign(new Error("Request failed with status code 401"), {
        response: { status: 401, data: { detail: "Invalid credentials" } },
      });
      (apiClient.post as jest.Mock).mockRejectedValue(unauthorizedError);

      await expect(authService.login("test@example.com", "wrong")).rejects.toThrow();
      expect(SecureStorage.setAccessToken).not.toHaveBeenCalled();
      expect(SecureStorage.setRefreshToken).not.toHaveBeenCalled();
      expect(SecureStorage.setUserId).not.toHaveBeenCalled();
    });
  });

  describe("completeMfa", () => {
    it("stores tokens and maps /me on status ok", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({
        data: { status: "ok", tokens: tokensResponse },
      });
      (apiClient.get as jest.Mock).mockResolvedValue({ data: meResponse });

      const outcome = await authService.completeMfa("mfa-token-abc", "123456", "test@example.com");

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/identity/auth/mfa/complete", {
        mfaToken: "mfa-token-abc",
        code: "123456",
      });
      expect(apiClient.get).toHaveBeenCalledWith("/api/v1/identity/me");
      expect(outcome.status).toBe("ok");
      expect(outcome.tokens).toEqual(tokensResponse);
      expect(SecureStorage.setAccessToken).toHaveBeenCalledWith("access-token");
      expect(SecureStorage.setRefreshToken).toHaveBeenCalledWith("refresh-token");
      expect(SecureStorage.setUserId).toHaveBeenCalledWith("user-123");
      expect(SecureStorage.setTenantId).toHaveBeenCalledWith("tenant-123");
      expect(PreferenceStorage.setLastLoginEmail).toHaveBeenCalledWith("test@example.com");
    });

    it("does not record a last-login email when none is given", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({
        data: { status: "ok", tokens: tokensResponse },
      });
      (apiClient.get as jest.Mock).mockResolvedValue({ data: meResponse });

      await authService.completeMfa("mfa-token-abc", "123456", null);

      expect(PreferenceStorage.setLastLoginEmail).not.toHaveBeenCalled();
    });

    it("throws identity.mfa.invalid_code without storing tokens or recording the email", async () => {
      const invalidCodeError = Object.assign(new Error("Request failed with status code 401"), {
        response: {
          status: 401,
          data: { code: "identity.mfa.invalid_code", detail: "Kod geçersiz." },
        },
      });
      (apiClient.post as jest.Mock).mockRejectedValue(invalidCodeError);

      await expect(
        authService.completeMfa("mfa-token-abc", "000000", "test@example.com")
      ).rejects.toThrow();
      expect(SecureStorage.setAccessToken).not.toHaveBeenCalled();
      expect(SecureStorage.setRefreshToken).not.toHaveBeenCalled();
      expect(PreferenceStorage.setLastLoginEmail).not.toHaveBeenCalled();
    });

    it("throws identity.mfa.token_expired without storing tokens or establishing a session", async () => {
      const expiredError = Object.assign(new Error("Request failed with status code 401"), {
        response: {
          status: 401,
          data: { code: "identity.mfa.token_expired", detail: "Süre doldu." },
        },
      });
      (apiClient.post as jest.Mock).mockRejectedValue(expiredError);

      await expect(
        authService.completeMfa("mfa-token-abc", "123456", "test@example.com")
      ).rejects.toThrow();
      expect(SecureStorage.setAccessToken).not.toHaveBeenCalled();
      expect(SecureStorage.setRefreshToken).not.toHaveBeenCalled();
      expect(apiClient.get).not.toHaveBeenCalled();
      expect(PreferenceStorage.setLastLoginEmail).not.toHaveBeenCalled();
    });
  });

  describe("forgotPassword", () => {
    it("posts the e-mail to the anonymous forgot-password endpoint", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: undefined });

      await authService.forgotPassword("test@example.com");

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/identity/auth/forgot-password", {
        email: "test@example.com",
      });
    });

    it("logs a fixed-text message (never the error object) and rethrows on failure", async () => {
      const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
      const serverError = Object.assign(new Error("Request failed with status code 500"), {
        isAxiosError: true,
        response: { status: 500, data: { detail: "Internal error" } },
      });
      (apiClient.post as jest.Mock).mockRejectedValue(serverError);

      await expect(authService.forgotPassword("test@example.com")).rejects.toThrow();

      expect(consoleError).toHaveBeenCalledWith("Forgot password request failed.");
      expect(consoleError).not.toHaveBeenCalledWith(serverError);
      consoleError.mockRestore();
    });
  });

  describe("logout", () => {
    it("sends the stored refresh token and allSessions: false", async () => {
      (SecureStorage.getRefreshToken as jest.Mock).mockResolvedValue("refresh-token");
      (apiClient.post as jest.Mock).mockResolvedValue({ data: {} });

      await authService.logout();

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/identity/auth/logout", {
        refreshToken: "refresh-token",
        allSessions: false,
      });
      expect(SecureStorage.clearAll).toHaveBeenCalled();
    });

    it("revokes this device's push token before clearing local storage (Bearer still valid)", async () => {
      (SecureStorage.getRefreshToken as jest.Mock).mockResolvedValue("refresh-token");
      (apiClient.post as jest.Mock).mockResolvedValue({ data: {} });
      const callOrder: string[] = [];
      (pushNotificationService.unregisterPushToken as jest.Mock).mockImplementation(async () => {
        callOrder.push("unregisterPushToken");
      });
      (SecureStorage.clearAll as jest.Mock).mockImplementation(async () => {
        callOrder.push("clearAll");
      });

      await authService.logout();

      expect(pushNotificationService.unregisterPushToken).toHaveBeenCalled();
      expect(callOrder).toEqual(["unregisterPushToken", "clearAll"]);
    });

    it("completes local logout even when unregisterPushToken rejects", async () => {
      (SecureStorage.getRefreshToken as jest.Mock).mockResolvedValue("refresh-token");
      (apiClient.post as jest.Mock).mockResolvedValue({ data: {} });
      (pushNotificationService.unregisterPushToken as jest.Mock).mockRejectedValue(
        new Error("network")
      );

      await expect(authService.logout()).resolves.toBeUndefined();

      expect(SecureStorage.clearAll).toHaveBeenCalled();
      expect(biometricService.disableBiometric).toHaveBeenCalled();
    });
  });

  describe("refreshToken", () => {
    it("posts the refresh token and stores the returned token pair", async () => {
      (SecureStorage.getRefreshToken as jest.Mock).mockResolvedValue("old-refresh-token");
      (apiClient.post as jest.Mock).mockResolvedValue({ data: tokensResponse });

      const result = await authService.refreshToken();

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/identity/auth/refresh", {
        refreshToken: "old-refresh-token",
      });
      expect(result).toEqual(tokensResponse);
      expect(SecureStorage.setAccessToken).toHaveBeenCalledWith("access-token");
      expect(SecureStorage.setRefreshToken).toHaveBeenCalledWith("refresh-token");
    });

    it("throws when no refresh token is stored", async () => {
      (SecureStorage.getRefreshToken as jest.Mock).mockResolvedValue(null);

      await expect(authService.refreshToken()).rejects.toThrow("No refresh token available");
      expect(apiClient.post).not.toHaveBeenCalled();
    });
  });

  describe("validateToken", () => {
    it("should validate token successfully", async () => {
      (SecureStorage.getAccessToken as jest.Mock).mockResolvedValue("valid-token");
      (apiClient.get as jest.Mock).mockResolvedValue({ data: meResponse });

      const result = await authService.validateToken();

      expect(result).toBe(true);
    });

    it("should return false if no token exists", async () => {
      (SecureStorage.getAccessToken as jest.Mock).mockResolvedValue(null);

      const result = await authService.validateToken();

      expect(result).toBe(false);
    });

    it("should return false when /me rejects the stored token (401)", async () => {
      (SecureStorage.getAccessToken as jest.Mock).mockResolvedValue("revoked-token");
      (apiClient.get as jest.Mock).mockRejectedValue(
        Object.assign(new Error("Request failed with status code 401"), {
          response: { status: 401 },
        })
      );

      const result = await authService.validateToken();

      expect(result).toBe(false);
    });
  });

  describe("enableBiometricLogin", () => {
    it("never logs the raw error object - only a fixed-text dev log - when it fails", async () => {
      const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
      (PreferenceStorage.getLastLoginEmail as jest.Mock).mockResolvedValue("test@example.com");
      (SecureStorage.getRefreshToken as jest.Mock).mockResolvedValue("secret-refresh-token");
      const leakyError = Object.assign(new Error("Biometric authentication failed"), {
        config: { headers: { Authorization: "Bearer secret-access-token" } },
      });
      (biometricService.enableBiometric as jest.Mock).mockRejectedValue(leakyError);

      await expect(authService.enableBiometricLogin()).rejects.toThrow();

      expect(consoleError).toHaveBeenCalledWith("Failed to enable biometric login.");
      for (const call of consoleError.mock.calls) {
        expect(call).not.toContain(leakyError);
        expect(JSON.stringify(call)).not.toMatch(/secret-/);
      }
      consoleError.mockRestore();
    });
  });

  describe("error logging", () => {
    it("never logs the raw error object when logout or /me fail", async () => {
      const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
      const leakyError = Object.assign(new Error("network"), {
        config: {
          data: '{"refreshToken":"secret-refresh"}',
          headers: { Authorization: "Bearer secret-access" },
        },
      });
      (SecureStorage.getRefreshToken as jest.Mock).mockResolvedValue("secret-refresh");
      (apiClient.post as jest.Mock).mockRejectedValue(leakyError);
      (apiClient.get as jest.Mock).mockRejectedValue(leakyError);

      await authService.logout();
      await authService.getCurrentUser();

      for (const call of consoleError.mock.calls) {
        expect(call).not.toContain(leakyError);
        expect(JSON.stringify(call)).not.toMatch(/secret-/);
      }
      consoleError.mockRestore();
    });

    it("never logs the raw error object when completeMfa fails (TOTP code, mfaToken)", async () => {
      const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
      const leakyError = Object.assign(new Error("Request failed with status code 401"), {
        config: { data: '{"mfaToken":"secret-mfa-token","code":"999999"}' },
        response: { status: 401, data: { code: "identity.mfa.invalid_code" } },
      });
      (apiClient.post as jest.Mock).mockRejectedValue(leakyError);

      await expect(
        authService.completeMfa("secret-mfa-token", "999999", "test@example.com")
      ).rejects.toThrow();

      for (const call of consoleError.mock.calls) {
        expect(call).not.toContain(leakyError);
        expect(JSON.stringify(call)).not.toMatch(/secret-mfa-token|999999/);
      }
      consoleError.mockRestore();
    });
  });

  describe("console.error suppression for expected user errors (B14/B15)", () => {
    it("does not log to console.error on a 401 invalid-credentials login failure", async () => {
      const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
      const unauthorizedError = Object.assign(new Error("Request failed with status code 401"), {
        isAxiosError: true,
        response: { status: 401, data: { code: "auth.invalid_credentials" } },
      });
      (apiClient.post as jest.Mock).mockRejectedValue(unauthorizedError);

      await expect(authService.login("test@example.com", "wrong")).rejects.toThrow();

      expect(consoleError).not.toHaveBeenCalled();
      consoleError.mockRestore();
    });

    it("logs a fixed-text message (never the error object) when login fails unexpectedly (network/5xx)", async () => {
      const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
      const serverError = Object.assign(new Error("Request failed with status code 500"), {
        isAxiosError: true,
        response: { status: 500, data: { detail: "Internal error" } },
      });
      (apiClient.post as jest.Mock).mockRejectedValue(serverError);

      await expect(authService.login("test@example.com", "password123")).rejects.toThrow();

      expect(consoleError).toHaveBeenCalledWith("Login failed.");
      expect(consoleError).not.toHaveBeenCalledWith(serverError);
      consoleError.mockRestore();
    });

    it("does not log to console.error on a 401 invalid_code MFA failure", async () => {
      const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
      const invalidCodeError = Object.assign(new Error("Request failed with status code 401"), {
        isAxiosError: true,
        response: { status: 401, data: { code: "identity.mfa.invalid_code" } },
      });
      (apiClient.post as jest.Mock).mockRejectedValue(invalidCodeError);

      await expect(
        authService.completeMfa("mfa-token-abc", "000000", "test@example.com")
      ).rejects.toThrow();

      expect(consoleError).not.toHaveBeenCalled();
      consoleError.mockRestore();
    });

    it("does not log to console.error on a 401 token_expired MFA failure", async () => {
      const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
      const expiredError = Object.assign(new Error("Request failed with status code 401"), {
        isAxiosError: true,
        response: { status: 401, data: { code: "identity.mfa.token_expired" } },
      });
      (apiClient.post as jest.Mock).mockRejectedValue(expiredError);

      await expect(
        authService.completeMfa("mfa-token-abc", "123456", "test@example.com")
      ).rejects.toThrow();

      expect(consoleError).not.toHaveBeenCalled();
      consoleError.mockRestore();
    });

    it("logs a fixed-text message (never the error object) when completeMfa fails unexpectedly (network/5xx)", async () => {
      const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
      const serverError = Object.assign(new Error("Request failed with status code 503"), {
        isAxiosError: true,
        response: { status: 503, data: { detail: "Service unavailable" } },
      });
      (apiClient.post as jest.Mock).mockRejectedValue(serverError);

      await expect(
        authService.completeMfa("mfa-token-abc", "123456", "test@example.com")
      ).rejects.toThrow();

      expect(consoleError).toHaveBeenCalledWith("MFA completion failed.");
      expect(consoleError).not.toHaveBeenCalledWith(serverError);
      consoleError.mockRestore();
    });
  });
});
