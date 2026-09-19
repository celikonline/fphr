import { describe, it, expect, vi, beforeEach } from "vitest";
import { useAuthStore } from "./auth.store";

// vi.mock factories are hoisted above regular top-level statements, so the mocked value must be
// created with vi.hoisted() to avoid a TDZ error.
const mockApiClient = vi.hoisted(() => ({
  post: vi.fn(),
}));

vi.mock("@senseik/api-client", () => ({
  apiClient: mockApiClient,
  AUTH_TOKEN_STORAGE_KEY: "auth_token",
}));

vi.mock("@/services/me.service", () => ({
  getMe: vi.fn(),
}));

vi.mock("@/lib/jwt", () => ({
  getImpersonatorId: () => null,
}));

const AUTH_BASE = "/v1/identity/auth";

describe("auth.store refresh() (S-53: token refresh, not just an instant logout)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    useAuthStore.setState({ token: null, refreshToken: null, user: null, features: null, error: null, isLoading: false });
  });

  it("returns false without calling the API when there is no refresh token in memory", async () => {
    const ok = await useAuthStore.getState().refresh();

    expect(ok).toBe(false);
    expect(mockApiClient.post).not.toHaveBeenCalled();
  });

  it("rotates the token pair and persists the new access token on success", async () => {
    useAuthStore.setState({ refreshToken: "old-refresh-token" });
    mockApiClient.post.mockResolvedValue({ data: { accessToken: "new-access-token", refreshToken: "new-refresh-token" } });

    const ok = await useAuthStore.getState().refresh();

    expect(ok).toBe(true);
    expect(mockApiClient.post).toHaveBeenCalledWith(
      `${AUTH_BASE}/refresh`,
      { refreshToken: "old-refresh-token" },
      expect.objectContaining({ skipAuthRetry: true }),
    );
    expect(useAuthStore.getState().token).toBe("new-access-token");
    expect(useAuthStore.getState().refreshToken).toBe("new-refresh-token");
    expect(localStorage.getItem("auth_token")).toBe("new-access-token");
  });

  it("returns false (never throws) when the backend rejects the refresh token", async () => {
    useAuthStore.setState({ refreshToken: "revoked-refresh-token" });
    mockApiClient.post.mockRejectedValue(new Error("401"));

    await expect(useAuthStore.getState().refresh()).resolves.toBe(false);
    // The old (now-invalid) tokens are left in place - api-client's handleResponseError is the one
    // that decides to end the session, so refresh() itself must not clear state.
    expect(useAuthStore.getState().refreshToken).toBe("revoked-refresh-token");
  });
});
