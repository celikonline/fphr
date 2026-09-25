import { authService } from "@/services/AuthService";
import { apiClient } from "@/lib/apiClient";
import { SecureStorage, PreferenceStorage } from "@/lib/storage";

jest.mock("@/lib/apiClient");
jest.mock("@/lib/storage");
jest.mock("@/lib/biometric");

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
  permissions: ["leave.view"]
};

// Mirrors AuthTokensDto.
const tokensResponse = {
  accessToken: "access-token",
  accessTokenExpiresAt: "2026-09-25T12:00:00Z",
  refreshToken: "refresh-token",
  refreshTokenExpiresAt: "2026-10-02T12:00:00Z",
  tokenType: "Bearer"
};

describe("AuthService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("login", () => {
    it("stores tokens and maps /me on status ok", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: { status: "ok", tokens: tokensResponse } });
      (apiClient.get as jest.Mock).mockResolvedValue({ data: meResponse });

      const outcome = await authService.login("test@example.com", "password123");

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/identity/auth/login", {
        email: "test@example.com",
        password: "password123",
        tenantSlug: undefined
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
          tenant: meResponse.tenant
        },
        tokens: tokensResponse
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
        { tenantId: "tenant-456", slug: "biriksin", name: "Biriksin" }
      ];
      (apiClient.post as jest.Mock).mockResolvedValue({ data: { status: "select_tenant", tenants } });

      const outcome = await authService.login("owner@demo.local", "Demo123!");

      expect(outcome).toEqual({ status: "select_tenant", tenants });
      expect(apiClient.get).not.toHaveBeenCalled();
      expect(SecureStorage.setAccessToken).not.toHaveBeenCalled();
      expect(SecureStorage.setRefreshToken).not.toHaveBeenCalled();
      expect(SecureStorage.setUserId).not.toHaveBeenCalled();
      expect(SecureStorage.setTenantId).not.toHaveBeenCalled();
    });

    it("re-submits the chosen tenantSlug and logs in", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: { status: "ok", tokens: tokensResponse } });
      (apiClient.get as jest.Mock).mockResolvedValue({ data: meResponse });

      const outcome = await authService.login("owner@demo.local", "Demo123!", "biriksin");

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/identity/auth/login", {
        email: "owner@demo.local",
        password: "Demo123!",
        tenantSlug: "biriksin"
      });
      expect(outcome.status).toBe("ok");
    });

    it("throws a Turkish, mobile-unsupported error on status mfa_required without storing tokens", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({
        data: { status: "mfa_required", mfaToken: "mfa-token-abc" }
      });

      await expect(authService.login("test@example.com", "password123")).rejects.toThrow(
        "İki adımlı doğrulama mobil uygulamada henüz desteklenmiyor. Lütfen web tarayıcısından giriş yapın."
      );
      expect(SecureStorage.setAccessToken).not.toHaveBeenCalled();
      expect(SecureStorage.setRefreshToken).not.toHaveBeenCalled();
    });

    it("throws without storing tokens on invalid credentials (401)", async () => {
      const unauthorizedError = Object.assign(new Error("Request failed with status code 401"), {
        response: { status: 401, data: { detail: "Invalid credentials" } }
      });
      (apiClient.post as jest.Mock).mockRejectedValue(unauthorizedError);

      await expect(authService.login("test@example.com", "wrong")).rejects.toThrow();
      expect(SecureStorage.setAccessToken).not.toHaveBeenCalled();
      expect(SecureStorage.setRefreshToken).not.toHaveBeenCalled();
      expect(SecureStorage.setUserId).not.toHaveBeenCalled();
    });
  });

  describe("logout", () => {
    it("sends the stored refresh token and allSessions: false", async () => {
      (SecureStorage.getRefreshToken as jest.Mock).mockResolvedValue("refresh-token");
      (apiClient.post as jest.Mock).mockResolvedValue({ data: {} });

      await authService.logout();

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/identity/auth/logout", {
        refreshToken: "refresh-token",
        allSessions: false
      });
      expect(SecureStorage.clearAll).toHaveBeenCalled();
    });
  });

  describe("refreshToken", () => {
    it("posts the refresh token and stores the returned token pair", async () => {
      (SecureStorage.getRefreshToken as jest.Mock).mockResolvedValue("old-refresh-token");
      (apiClient.post as jest.Mock).mockResolvedValue({ data: tokensResponse });

      const result = await authService.refreshToken();

      expect(apiClient.post).toHaveBeenCalledWith("/api/v1/identity/auth/refresh", {
        refreshToken: "old-refresh-token"
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
        Object.assign(new Error("Request failed with status code 401"), { response: { status: 401 } })
      );

      const result = await authService.validateToken();

      expect(result).toBe(false);
    });
  });

  describe("error logging", () => {
    it("never logs the raw error object when logout or /me fail", async () => {
      const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
      const leakyError = Object.assign(new Error("network"), {
        config: { data: '{"refreshToken":"secret-refresh"}', headers: { Authorization: "Bearer secret-access" } }
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
  });
});
