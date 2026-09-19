import { describe, it, expect, vi, beforeEach } from "vitest";
import type { AxiosError } from "axios";

// `@senseik/api-client` creates its axios instance at module load time via `axios.create(...)`.
// Mocking `create` to always return this hoisted, fully-controlled instance lets the test exercise
// the REAL `handleResponseError` (imported fresh per test below) against a fake HTTP layer, instead
// of re-implementing/duplicating its refresh-retry logic as a second, parallel mock (as
// http-interceptors.test.ts does for the web-only 429 interceptor).
const mockInstance = vi.hoisted(() => ({
  interceptors: {
    request: { use: vi.fn() },
    response: { use: vi.fn() },
  },
  request: vi.fn(),
}));

vi.mock("axios", async () => {
  const actual = await vi.importActual<typeof import("axios")>("axios");
  return {
    default: {
      ...actual,
      create: () => mockInstance,
    },
  };
});

type ApiClientModule = typeof import("@senseik/api-client");

const TOKEN_KEY = "auth_token";
const STORE_KEY = "auth-store";

function authError(status: number, extraConfig: Record<string, unknown> = {}): AxiosError {
  return {
    response: { status },
    config: { headers: { Authorization: "Bearer stale" }, ...extraConfig },
  } as unknown as AxiosError;
}

describe("api-client 401 handling (S-53: silent session drops)", () => {
  let apiClientModule: ApiClientModule;

  beforeEach(async () => {
    vi.clearAllMocks();
    localStorage.clear();
    // Fresh module instance per test: `handlingSessionExpiry`/`refreshInFlight` are module-level
    // state that must not leak between scenarios (mirrors the pattern in http-interceptors.test.ts).
    vi.resetModules();
    apiClientModule = await import("@senseik/api-client");
  });

  it("passes a 401 from an anonymous request straight through (no refresh attempt)", async () => {
    const refreshHandler = vi.fn();
    apiClientModule.setRefreshHandler(refreshHandler);
    const error = { response: { status: 401 }, config: { headers: {} } } as unknown as AxiosError;

    await expect(apiClientModule.handleResponseError(error)).rejects.toBe(error);
    expect(refreshHandler).not.toHaveBeenCalled();
  });

  it("refreshes once and retries the original request on a 401 from an authenticated request", async () => {
    apiClientModule.setRefreshHandler(async () => {
      localStorage.setItem(TOKEN_KEY, "new-token");
      return true;
    });
    mockInstance.request.mockResolvedValue({ status: 200, data: { ok: true } });

    const error = authError(401);
    const response = await apiClientModule.handleResponseError(error);

    expect(response).toEqual({ status: 200, data: { ok: true } });
    expect(mockInstance.request).toHaveBeenCalledTimes(1);
    expect(mockInstance.request).toHaveBeenCalledWith(expect.objectContaining({ _retriedAfterRefresh: true }));
  });

  it("ends the session and warns the user exactly once when the refresh itself fails", async () => {
    const sessionExpired = vi.fn();
    apiClientModule.setRefreshHandler(async () => false);
    apiClientModule.setSessionExpiredHandler(sessionExpired);
    // jsdom's `window.location.assign` isn't a configurable property, so it can't be `vi.spyOn`-ed
    // directly; letting the real (jsdom-stubbed, no-op) navigation happen and asserting the
    // side effects we actually care about is simpler than replacing all of `window.location`.
    localStorage.setItem(TOKEN_KEY, "stale-token");
    localStorage.setItem(STORE_KEY, "{}");

    const error = authError(401);
    await expect(apiClientModule.handleResponseError(error)).rejects.toBe(error);

    expect(sessionExpired).toHaveBeenCalledTimes(1);
    // Both the access token and the persisted zustand store are cleared, matching the pre-existing
    // clearSession() contract - otherwise the route guard would still see a "logged-in" user.
    expect(localStorage.getItem(TOKEN_KEY)).toBeNull();
    expect(localStorage.getItem(STORE_KEY)).toBeNull();
  });

  it("dedupes concurrent 401s (several requests failing at once) into a single refresh call", async () => {
    let resolveRefresh!: (ok: boolean) => void;
    const refreshHandler = vi.fn(
      () =>
        new Promise<boolean>((resolve) => {
          resolveRefresh = resolve;
        }),
    );
    apiClientModule.setRefreshHandler(refreshHandler);
    mockInstance.request.mockResolvedValue({ status: 200 });

    const first = apiClientModule.handleResponseError(authError(401));
    const second = apiClientModule.handleResponseError(authError(401));
    resolveRefresh(true);
    await Promise.all([first, second]);

    expect(refreshHandler).toHaveBeenCalledTimes(1);
    expect(mockInstance.request).toHaveBeenCalledTimes(2);
  });

  it("does not attempt a second refresh once a retried request 401s again", async () => {
    const refreshHandler = vi.fn();
    apiClientModule.setRefreshHandler(refreshHandler);
    const sessionExpired = vi.fn();
    apiClientModule.setSessionExpiredHandler(sessionExpired);

    const error = authError(401, { _retriedAfterRefresh: true });
    await expect(apiClientModule.handleResponseError(error)).rejects.toBe(error);

    expect(refreshHandler).not.toHaveBeenCalled();
    expect(sessionExpired).toHaveBeenCalledTimes(1);
  });

  it("does not re-enter the refresh flow for the refresh call's own 401 (skipAuthRetry)", async () => {
    const refreshHandler = vi.fn();
    apiClientModule.setRefreshHandler(refreshHandler);

    const error = authError(401, { skipAuthRetry: true });
    await expect(apiClientModule.handleResponseError(error)).rejects.toBe(error);

    expect(refreshHandler).not.toHaveBeenCalled();
  });
});
