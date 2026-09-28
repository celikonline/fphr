import { useAuthStore } from "@/store/authStore";
import { authService, isExpectedAuthFailure } from "@/services/AuthService";
import { SecureStorage } from "@/lib/storage";
import type { AuthTokens, User } from "@/types";

jest.mock("@/services/AuthService");
jest.mock("@/lib/storage");
jest.mock("@/lib/biometric");

const initialState = useAuthStore.getState();

const user: User = {
  id: "user-123",
  tenantId: "tenant-123",
  email: "test@example.com",
  firstName: "John",
  lastName: "Doe",
  employeeId: null,
  preferredLanguage: "tr",
  mfaEnabled: true,
  roles: ["employee"],
  permissions: ["leave.view"],
  tenant: { id: "tenant-123", name: "Demo Co", slug: "demo-hr-company" },
};

const tokens: AuthTokens = {
  accessToken: "access-token",
  accessTokenExpiresAt: "2026-09-25T12:00:00Z",
  refreshToken: "refresh-token",
  refreshTokenExpiresAt: "2026-10-02T12:00:00Z",
  tokenType: "Bearer",
};

describe("authStore", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    // `true` replaces the whole state so a previous test's mfaToken/isAuthenticated can't leak.
    useAuthStore.setState(initialState, true);
  });

  describe("login", () => {
    it("stores the mfaToken and its email, and does not authenticate, when the outcome is mfaRequired", async () => {
      (authService.login as jest.Mock).mockResolvedValue({
        status: "mfaRequired",
        mfaToken: "mfa-token-abc",
      });

      const outcome = await useAuthStore.getState().login("test@example.com", "password123");

      expect(outcome).toEqual({ status: "mfaRequired", mfaToken: "mfa-token-abc" });
      const state = useAuthStore.getState();
      expect(state.mfaToken).toBe("mfa-token-abc");
      expect(state.mfaEmail).toBe("test@example.com");
      expect(state.isAuthenticated).toBe(false);
      expect(state.tokens).toBeNull();
    });

    it("clears any stale mfaToken/mfaEmail when the outcome is select_tenant", async () => {
      useAuthStore.setState({ mfaToken: "stale-token", mfaEmail: "stale@example.com" });
      (authService.login as jest.Mock).mockResolvedValue({
        status: "select_tenant",
        tenants: [],
      });

      await useAuthStore.getState().login("test@example.com", "password123");

      const state = useAuthStore.getState();
      expect(state.mfaToken).toBeNull();
      expect(state.mfaEmail).toBeNull();
    });

    it("clears any stale mfaToken/mfaEmail on a direct ok login", async () => {
      useAuthStore.setState({ mfaToken: "stale-token", mfaEmail: "stale@example.com" });
      (authService.login as jest.Mock).mockResolvedValue({ status: "ok", user, tokens });

      await useAuthStore.getState().login("test@example.com", "password123");

      const state = useAuthStore.getState();
      expect(state.mfaToken).toBeNull();
      expect(state.mfaEmail).toBeNull();
      expect(state.isAuthenticated).toBe(true);
    });
  });

  describe("completeMfa", () => {
    it("throws and never calls the service when no mfaToken is pending", async () => {
      useAuthStore.setState({ mfaToken: null, mfaEmail: null });

      await expect(useAuthStore.getState().completeMfa("123456")).rejects.toThrow();
      expect(authService.completeMfa).not.toHaveBeenCalled();
    });

    it("passes the paired email to the service, authenticates, and clears the challenge when the code is accepted", async () => {
      useAuthStore.setState({ mfaToken: "mfa-token-abc", mfaEmail: "user@example.com" });
      (authService.completeMfa as jest.Mock).mockResolvedValue({ status: "ok", user, tokens });

      await useAuthStore.getState().completeMfa("123456");

      expect(authService.completeMfa).toHaveBeenCalledWith(
        "mfa-token-abc",
        "123456",
        "user@example.com"
      );
      const state = useAuthStore.getState();
      expect(state.isAuthenticated).toBe(true);
      expect(state.user).toEqual(user);
      expect(state.tokens).toEqual(tokens);
      expect(state.mfaToken).toBeNull();
      expect(state.mfaEmail).toBeNull();
    });

    it("keeps the mfaToken and mfaEmail (so the user can retry) when the code is rejected", async () => {
      useAuthStore.setState({ mfaToken: "mfa-token-abc", mfaEmail: "user@example.com" });
      (authService.completeMfa as jest.Mock).mockRejectedValue(new Error("invalid code"));

      await expect(useAuthStore.getState().completeMfa("000000")).rejects.toThrow();

      const state = useAuthStore.getState();
      expect(state.mfaToken).toBe("mfa-token-abc");
      expect(state.mfaEmail).toBe("user@example.com");
      expect(state.isAuthenticated).toBe(false);
      expect(state.tokens).toBeNull();
    });

    it("does not authenticate when the challenge token expired", async () => {
      useAuthStore.setState({ mfaToken: "mfa-token-abc", mfaEmail: "user@example.com" });
      (authService.completeMfa as jest.Mock).mockRejectedValue(new Error("token expired"));

      await expect(useAuthStore.getState().completeMfa("123456")).rejects.toThrow();

      const state = useAuthStore.getState();
      expect(state.isAuthenticated).toBe(false);
      expect(state.tokens).toBeNull();
      expect(state.user).toBeNull();
    });
  });

  describe("clearMfaChallenge", () => {
    it("drops the pending mfaToken, mfaEmail, and any error", () => {
      useAuthStore.setState({
        mfaToken: "mfa-token-abc",
        mfaEmail: "user@example.com",
        error: "some error",
      });

      useAuthStore.getState().clearMfaChallenge();

      const state = useAuthStore.getState();
      expect(state.mfaToken).toBeNull();
      expect(state.mfaEmail).toBeNull();
      expect(state.error).toBeNull();
    });
  });

  describe("checkAuth", () => {
    beforeEach(() => {
      (SecureStorage.getAccessToken as jest.Mock).mockResolvedValue("stored-token");
    });

    it("does not log to console.error at all on an expected 401 (expired/invalid access token)", async () => {
      const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
      const unauthorizedError = Object.assign(new Error("Request failed with status code 401"), {
        isAxiosError: true,
        response: { status: 401 },
      });
      (authService.validateToken as jest.Mock).mockRejectedValue(unauthorizedError);
      (isExpectedAuthFailure as jest.Mock).mockReturnValue(true);

      await useAuthStore.getState().checkAuth();

      expect(consoleError).not.toHaveBeenCalled();
      expect(useAuthStore.getState().isAuthenticated).toBe(false);
      consoleError.mockRestore();
    });

    it("logs a fixed-text message - never the raw error object - on an unexpected failure", async () => {
      const consoleError = jest.spyOn(console, "error").mockImplementation(() => undefined);
      // A network failure could otherwise carry the Authorization header via `error.config.headers`.
      const networkError = Object.assign(new Error("Network Error"), {
        isAxiosError: true,
        config: { headers: { Authorization: "Bearer secret-access-token" } },
      });
      (authService.validateToken as jest.Mock).mockRejectedValue(networkError);
      (isExpectedAuthFailure as jest.Mock).mockReturnValue(false);

      await useAuthStore.getState().checkAuth();

      expect(consoleError).toHaveBeenCalledWith("Auth check failed.");
      for (const call of consoleError.mock.calls) {
        expect(call).not.toContain(networkError);
        expect(JSON.stringify(call)).not.toMatch(/secret-access-token/);
      }
      expect(useAuthStore.getState().isAuthenticated).toBe(false);
      consoleError.mockRestore();
    });
  });
});
