import { apiClient, setUnauthorizedHandler } from "@/lib/apiClient";
import { SecureStorage } from "@/lib/storage";
import { isExpectedAuthFailure } from "@/services/AuthService";

jest.mock("@/lib/storage");

/**
 * `apiClient` is normally imported through `jest.mock("@/lib/apiClient")` by the service tests, so
 * this is the one place the real axios instance's defaults get exercised. Reaches into the private
 * `instance` field on purpose (there is no public getter, same as `getBaseURL()` does internally) -
 * this is a regression guard for D4 (leave/error responses must come back localized in Turkish).
 */
describe("apiClient default headers", () => {
  it("sends Accept-Language: tr on every request (the app has no i18n and is Turkish-only)", () => {
    const instance = (
      apiClient as unknown as { instance: { defaults: { headers: Record<string, unknown> } } }
    ).instance;

    expect(instance.defaults.headers["Accept-Language"]).toBe("tr");
  });
});

/**
 * B16 regression: the response interceptor's 401 handler used to treat EVERY 401 as an expired
 * access token and try a refresh - including 401s from anonymous auth endpoints (wrong password,
 * wrong TOTP code), which never carried an access token to begin with. That silently retried the
 * SAME login/MFA request with a leftover refresh token's new access token, and/or logged out the
 * user via `onUnauthorized` for what was just a typo in the password field.
 *
 * These tests call the interceptor's rejection handler directly (axios exposes registered
 * interceptors via `interceptors.response.handlers`) instead of mocking HTTP at the transport layer -
 * no new test dependency (axios-mock-adapter/nock/msw) needed for a single interceptor function.
 */
describe("apiClient response interceptor - 401 handling (B16)", () => {
  type Handler = { rejected: (error: unknown) => Promise<unknown> };

  function getResponseErrorHandler(): (error: unknown) => Promise<unknown> {
    const instance = (
      apiClient as unknown as { instance: { interceptors: { response: { handlers: Handler[] } } } }
    ).instance;
    return instance.interceptors.response.handlers[0].rejected;
  }

  function makeAxiosError(config: { url: string; headers: Record<string, unknown> }) {
    return Object.assign(new Error("Request failed with status code 401"), {
      isAxiosError: true,
      config,
      response: { status: 401, data: {} },
    });
  }

  const onUnauthorized = jest.fn();

  beforeEach(() => {
    jest.clearAllMocks();
    setUnauthorizedHandler(onUnauthorized);
  });

  afterAll(() => {
    setUnauthorizedHandler(null);
  });

  it("rejects a login 401 unchanged, without attempting a refresh or triggering onUnauthorized", async () => {
    const rejected = getResponseErrorHandler();
    const error = makeAxiosError({ url: "/api/v1/identity/auth/login", headers: {} });

    await expect(rejected(error)).rejects.toBe(error);

    expect(SecureStorage.getRefreshToken).not.toHaveBeenCalled();
    expect(SecureStorage.clearAll).not.toHaveBeenCalled();
    expect(onUnauthorized).not.toHaveBeenCalled();
    // Downstream (AuthService) must see this as an expected user error, not an app failure.
    expect(isExpectedAuthFailure(error)).toBe(true);
  });

  it("rejects an mfa/complete 401 unchanged, without attempting a refresh", async () => {
    const rejected = getResponseErrorHandler();
    const error = makeAxiosError({ url: "/api/v1/identity/auth/mfa/complete", headers: {} });

    await expect(rejected(error)).rejects.toBe(error);

    expect(SecureStorage.getRefreshToken).not.toHaveBeenCalled();
    expect(onUnauthorized).not.toHaveBeenCalled();
  });

  it("rejects a 401 on any request with no Authorization header unchanged, without attempting a refresh", async () => {
    const rejected = getResponseErrorHandler();
    const error = makeAxiosError({ url: "/api/v1/some/public/endpoint", headers: {} });

    await expect(rejected(error)).rejects.toBe(error);

    expect(SecureStorage.getRefreshToken).not.toHaveBeenCalled();
    expect(onUnauthorized).not.toHaveBeenCalled();
  });

  it("still attempts a refresh for a 401 on a normal Bearer-authenticated request", async () => {
    (SecureStorage.getRefreshToken as jest.Mock).mockResolvedValue(null);
    const rejected = getResponseErrorHandler();
    const error = makeAxiosError({
      url: "/api/v1/leave/me/requests",
      headers: { Authorization: "Bearer old-access-token" },
    });

    await expect(rejected(error)).rejects.toThrow("No refresh token available");

    // No refresh token stored -> the refresh attempt itself fails, same as before B16: this proves
    // the refresh path was actually entered (unlike the anonymous-auth cases above).
    expect(SecureStorage.getRefreshToken).toHaveBeenCalled();
    expect(SecureStorage.clearAll).toHaveBeenCalled();
    expect(onUnauthorized).toHaveBeenCalled();
  });
});
