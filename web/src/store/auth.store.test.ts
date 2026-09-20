import { describe, expect, it, vi, beforeEach } from "vitest";
import { waitFor } from "@testing-library/react";
import { toUser, useAuthStore } from "./auth.store";
import type { MeResponse } from "@/services/me.service";

// senseik-040: "Profilim" (account.tsx) and "Kullanıcılar → Roller" (users-roles.tsx) used to show different
// Turkish text for the same role (e.g. Admin: "Yönetici" vs "Sistem Yöneticisi") because account.tsx translated
// the role CODE client-side while the Roller screen showed the backend's already-localized name. MeController now
// localizes `/me`'s roles the same way RolesController does (SharedResource.resx "roles.{code}.name"), so the
// frontend just has to carry that name through - never re-derive it - which is what `toUser` does below.
function meResponseWithRoles(roles: MeResponse["roles"]): MeResponse {
  return {
    userId: "user-1",
    email: "eren@example.com",
    firstName: "Eren",
    lastName: "Yılmaz",
    employeeId: null,
    preferredLanguage: "tr",
    mfaEnabled: false,
    tenant: {
      id: "tenant-1",
      slug: "acme",
      name: "Acme",
      taxNumber: null,
      status: "active",
      trialEndsAt: null,
      locale: "tr-TR",
      timeZone: "Europe/Istanbul",
    },
    roles,
    permissions: [],
    scopes: {},
    features: null,
  };
}

describe("toUser (senseik-040 role name consistency)", () => {
  it("carries the backend-localized role name through as roleLabels, in the same order as roles", () => {
    const me = meResponseWithRoles([
      { id: "role-hr", code: "HR", name: "İK Yöneticisi" },
      { id: "role-mgr", code: "Manager", name: "Yönetici" },
    ]);

    const user = toUser(me);

    expect(user.roles).toEqual(["HR", "Manager"]);
    // These must be the exact resx-backed strings RolesController would show for the same codes - not a
    // separately-maintained frontend translation table (that's the bug this fix removes).
    expect(user.roleLabels).toEqual(["İK Yöneticisi", "Yönetici"]);
  });

  it("falls back to the role code when the backend sends no name (never blank)", () => {
    const me = meResponseWithRoles([{ id: "role-custom", code: "CustomOpsRole", name: null }]);

    const user = toUser(me);

    expect(user.roleLabels).toEqual(["CustomOpsRole"]);
  });
});

// senseik-042: `hasHydrated` is what lets ProtectedRoute tell "no session yet" apart from "session
// not restored yet" - if the `onRehydrateStorage` wiring that flips it is ever removed or broken,
// the store would stay `hasHydrated: false` forever and every authenticated page would hang on the
// loading screen. This only asserts the flag eventually settles to `true` on its own; it does not
// pin the exact tick it happens on, since that is an implementation detail of zustand's `persist`.
describe("auth store hydration (senseik-042)", () => {
  it("flips hasHydrated to true once the persist middleware has read storage", async () => {
    await waitFor(() => expect(useAuthStore.getState().hasHydrated).toBe(true));
  });
});

// vi.mock factories are hoisted above regular top-level statements, so the mocked value must be
// created with vi.hoisted() to avoid a TDZ error.
const mockApiClient = vi.hoisted(() => ({
  post: vi.fn(),
}));

vi.mock("@senseik/api-client", () => ({
  apiClient: mockApiClient,
  AUTH_TOKEN_STORAGE_KEY: "auth_token",
  REFRESH_TOKEN_STORAGE_KEY: "refresh_token",
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
    sessionStorage.clear();
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
    // Rotated refresh token is kept in sessionStorage (not localStorage) so it survives F5 but not the tab.
    expect(sessionStorage.getItem("refresh_token")).toBe("new-refresh-token");
    expect(localStorage.getItem("refresh_token")).toBeNull();
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

// ADR-0009: the refresh token lives in sessionStorage so a full page reload (F5) keeps the session refreshable.
// `vi.resetModules()` + a fresh import stands in for the page reload: module state is rebuilt from storage only.
function fakeJwt(expiresInSeconds: number): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + expiresInSeconds }));
  return `header.${payload}.signature`;
}

type AuthStoreModule = typeof import("./auth.store");

async function reloadStore(): Promise<AuthStoreModule> {
  vi.resetModules();
  return import("./auth.store");
}

describe("auth.store refresh token storage (ADR-0009: F5 must not end the session)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    sessionStorage.clear();
  });

  it("restores the refresh token from sessionStorage after a reload, so refresh() works instead of returning false", async () => {
    localStorage.setItem("auth_token", fakeJwt(600));
    sessionStorage.setItem("refresh_token", "tab-refresh-token");
    mockApiClient.post.mockResolvedValue({ data: { accessToken: "rotated-access", refreshToken: "rotated-refresh" } });

    const { useAuthStore: reloaded } = await reloadStore();

    expect(reloaded.getState().refreshToken).toBe("tab-refresh-token");
    await expect(reloaded.getState().refresh()).resolves.toBe(true);
    expect(mockApiClient.post).toHaveBeenCalledWith(
      `${AUTH_BASE}/refresh`,
      { refreshToken: "tab-refresh-token" },
      expect.objectContaining({ skipAuthRetry: true }),
    );
    expect(sessionStorage.getItem("refresh_token")).toBe("rotated-refresh");
  });

  it("never persists the refresh token in localStorage (any copy there outlives the tab and is XSS-exfiltrable)", async () => {
    mockApiClient.post.mockResolvedValue({ data: { accessToken: "a", refreshToken: "rotated-secret" } });
    sessionStorage.setItem("refresh_token", "seed");
    const { useAuthStore: reloaded } = await reloadStore();

    await reloaded.getState().refresh();
    reloaded.getState().setToken("access", "explicit-secret");

    const localDump = JSON.stringify({ ...localStorage });
    expect(localDump).not.toContain("rotated-secret");
    expect(localDump).not.toContain("explicit-secret");
    expect(sessionStorage.getItem("refresh_token")).toBe("explicit-secret");
  });

  it("refreshes an already-expired access token during hydration and only then reports hasHydrated", async () => {
    localStorage.setItem("auth_token", fakeJwt(-60));
    sessionStorage.setItem("refresh_token", "tab-refresh-token");
    let resolveRefresh!: (value: unknown) => void;
    mockApiClient.post.mockReturnValue(new Promise((resolve) => (resolveRefresh = resolve)));

    const { useAuthStore: reloaded } = await reloadStore();

    // Refresh still in flight: the route guard must keep showing its loader, not bounce to /login.
    expect(reloaded.getState().hasHydrated).toBe(false);
    const freshAccess = fakeJwt(900);
    resolveRefresh({ data: { accessToken: freshAccess, refreshToken: "rotated-refresh" } });

    await waitFor(() => expect(reloaded.getState().hasHydrated).toBe(true));
    expect(reloaded.getState().token).toBe(freshAccess);
    expect(localStorage.getItem("auth_token")).toBe(freshAccess);
  });

  it("does not call the API on hydration when the access token is still valid", async () => {
    localStorage.setItem("auth_token", fakeJwt(600));
    sessionStorage.setItem("refresh_token", "tab-refresh-token");

    const { useAuthStore: reloaded } = await reloadStore();

    await waitFor(() => expect(reloaded.getState().hasHydrated).toBe(true));
    expect(mockApiClient.post).not.toHaveBeenCalled();
  });

  it("still reports hasHydrated when the expired session's refresh token is rejected", async () => {
    localStorage.setItem("auth_token", fakeJwt(-60));
    sessionStorage.setItem("refresh_token", "revoked");
    mockApiClient.post.mockRejectedValue(new Error("401"));

    const { useAuthStore: reloaded } = await reloadStore();

    await waitFor(() => expect(reloaded.getState().hasHydrated).toBe(true));
    expect(reloaded.getState().isAuthenticated()).toBe(false);
  });

  it("logout() clears every client-side copy (access token, refresh token, in-memory state)", async () => {
    localStorage.setItem("auth_token", fakeJwt(600));
    sessionStorage.setItem("refresh_token", "tab-refresh-token");
    mockApiClient.post.mockResolvedValue({ data: undefined });
    const { useAuthStore: reloaded } = await reloadStore();

    reloaded.getState().logout();

    expect(localStorage.getItem("auth_token")).toBeNull();
    expect(sessionStorage.getItem("refresh_token")).toBeNull();
    expect(reloaded.getState().token).toBeNull();
    expect(reloaded.getState().refreshToken).toBeNull();
    expect(reloaded.getState().user).toBeNull();
  });

  it("logout() best-effort revokes the refresh token server-side with an explicit bearer header", async () => {
    const access = fakeJwt(600);
    localStorage.setItem("auth_token", access);
    sessionStorage.setItem("refresh_token", "tab-refresh-token");
    mockApiClient.post.mockResolvedValue({ data: undefined });
    const { useAuthStore: reloaded } = await reloadStore();

    reloaded.getState().logout();

    expect(mockApiClient.post).toHaveBeenCalledWith(
      `${AUTH_BASE}/logout`,
      { refreshToken: "tab-refresh-token", allSessions: false },
      expect.objectContaining({ headers: { Authorization: `Bearer ${access}` }, skipAuthRetry: true }),
    );
  });

  it("logout() skips the server call when the access token is already expired and never throws", async () => {
    localStorage.setItem("auth_token", fakeJwt(-60));
    sessionStorage.setItem("refresh_token", "tab-refresh-token");
    mockApiClient.post.mockRejectedValue(new Error("network"));
    const { useAuthStore: reloaded } = await reloadStore();
    await waitFor(() => expect(reloaded.getState().hasHydrated).toBe(true));
    mockApiClient.post.mockClear();

    expect(() => reloaded.getState().logout()).not.toThrow();
    expect(mockApiClient.post).not.toHaveBeenCalled();
    expect(sessionStorage.getItem("refresh_token")).toBeNull();
  });
});
