import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
// Imported dynamically per test (see beforeEach below) instead of statically: `vi.resetModules()`
// cannot reset a module that's already bound via a top-level `import`, since that binding keeps
// pointing at the first-evaluated instance. Re-importing after resetModules() gives each test a
// fresh copy of the module, and with it a fresh `lastToastAt` (the throttle window's state).
type HttpInterceptorsModule = typeof import("./http-interceptors");

// Mock axios
vi.mock("axios", async () => {
  const actual = await vi.importActual("axios");
  return {
    default: {
      ...actual,
      // Duck-type on shape rather than `instanceof Error`: the fixtures below are plain
      // objects (like a JSON-cloned error crossing a worker/serialization boundary), not
      // real AxiosError instances, mirroring what axios's own isAxiosError checks for.
      isAxiosError: (error: unknown): boolean =>
        typeof error === "object" && error !== null && "response" in error && "config" in error,
    },
  };
});

// Mock @senseik/api-client
// vi.mock factories are hoisted above regular top-level statements, so the
// mocked values referenced inside them must be created with vi.hoisted().
const mockApiClient = vi.hoisted(() => ({
  interceptors: {
    response: {
      use: vi.fn(),
    },
  },
}));
const mockSetRefreshHandler = vi.hoisted(() => vi.fn());
const mockSetSessionExpiredHandler = vi.hoisted(() => vi.fn());

vi.mock("@senseik/api-client", () => ({
  apiClient: mockApiClient,
  setRefreshHandler: mockSetRefreshHandler,
  setSessionExpiredHandler: mockSetSessionExpiredHandler,
}));

// Mock the auth store: registerSessionRefreshHandlers() only needs `useAuthStore.getState().refresh`.
const mockAuthRefresh = vi.hoisted(() => vi.fn());
vi.mock("@/store/auth.store", () => ({
  useAuthStore: { getState: () => ({ refresh: mockAuthRefresh }) },
}));

// Mock toast hook
const mockToast = vi.hoisted(() => vi.fn());
vi.mock("@/hooks/use-toast", () => ({
  toast: mockToast,
}));

// Mock i18n
vi.mock("@/i18n", () => ({
  default: {
    t: (key: string, options?: { seconds: number }) =>
      `${key}${options?.seconds ? `: wait ${options.seconds}s` : ""}`,
  },
}));

describe("http-interceptors", () => {
  let httpInterceptorsModule: HttpInterceptorsModule;

  beforeEach(async () => {
    vi.clearAllMocks();
    // Reset module-level state (lastToastAt) by re-importing a fresh module instance.
    vi.resetModules();
    httpInterceptorsModule = await import("./http-interceptors");
  });

  afterEach(() => {
    vi.clearAllTimers();
  });

  it("registers response interceptor on apiClient", () => {
    httpInterceptorsModule.registerHttpInterceptors();
    expect(mockApiClient.interceptors.response.use).toHaveBeenCalled();
  });

  describe("429 rate limit handling", () => {
    it("shows toast on 429 response with Retry-After header", async () => {
      httpInterceptorsModule.registerHttpInterceptors();
      const [, errorHandler] = mockApiClient.interceptors.response.use.mock.calls[0];

      const error = {
        response: {
          status: 429,
          headers: {
            "retry-after": "30",
          },
        },
        config: { headers: { Authorization: "Bearer token" } },
      };

      Object.defineProperty(error, Symbol.toStringTag, {
        value: "AxiosError",
      });

      const result = errorHandler(error);

      expect(mockToast).toHaveBeenCalledWith({
        variant: "destructive",
        description: expect.stringContaining("30"),
      });
      // Awaiting (rather than `toEqual(Promise.reject(error))`, which never attaches a handler
      // to either promise) confirms the rejection and avoids an unhandled rejection warning.
      await expect(result).rejects.toBe(error);
    });

    it("uses default retry-after when header is missing", () => {
      httpInterceptorsModule.registerHttpInterceptors();
      const [, errorHandler] = mockApiClient.interceptors.response.use.mock.calls[0];

      const error = {
        response: {
          status: 429,
          headers: {},
        },
        config: { headers: { Authorization: "Bearer token" } },
      };

      Object.defineProperty(error, Symbol.toStringTag, {
        value: "AxiosError",
      });

      vi.useFakeTimers();
      // .catch() is a no-op here (the test only cares about the toast side effect) but avoids
      // an unhandled rejection warning, since the interceptor always rejects the promise.
      errorHandler(error).catch(() => {});
      vi.useRealTimers();

      expect(mockToast).toHaveBeenCalledWith({
        variant: "destructive",
        description: expect.stringContaining("60"), // DEFAULT_RETRY_AFTER_SECONDS
      });
    });

    it("throttles toast notifications (4 second window)", () => {
      vi.useFakeTimers();
      httpInterceptorsModule.registerHttpInterceptors();
      const [, errorHandler] = mockApiClient.interceptors.response.use.mock.calls[0];

      const error = {
        response: {
          status: 429,
          headers: { "retry-after": "30" },
        },
        config: { headers: { Authorization: "Bearer token" } },
      };

      Object.defineProperty(error, Symbol.toStringTag, {
        value: "AxiosError",
      });

      // First error: toast should show
      errorHandler(error).catch(() => {});
      expect(mockToast).toHaveBeenCalledTimes(1);

      // Second error immediately after: toast should NOT show (throttled)
      errorHandler(error).catch(() => {});
      expect(mockToast).toHaveBeenCalledTimes(1);

      // Advance time past TOAST_THROTTLE_MS (4000 ms)
      vi.advanceTimersByTime(4001);

      // Third error: toast should show again
      errorHandler(error).catch(() => {});
      expect(mockToast).toHaveBeenCalledTimes(2);

      vi.useRealTimers();
    });

    it("parses retry-after from HTTP date string", () => {
      httpInterceptorsModule.registerHttpInterceptors();
      const [, errorHandler] = mockApiClient.interceptors.response.use.mock.calls[0];

      const futureDate = new Date(Date.now() + 45000); // 45 seconds from now
      const error = {
        response: {
          status: 429,
          headers: {
            "retry-after": futureDate.toUTCString(),
          },
        },
        config: { headers: { Authorization: "Bearer token" } },
      };

      Object.defineProperty(error, Symbol.toStringTag, {
        value: "AxiosError",
      });

      errorHandler(error).catch(() => {});

      expect(mockToast).toHaveBeenCalledWith({
        variant: "destructive",
        description: expect.stringMatching(/4[0-9]|45/), // ~45 seconds
      });
    });

    it("ignores non-429 errors", async () => {
      httpInterceptorsModule.registerHttpInterceptors();
      const [, errorHandler] = mockApiClient.interceptors.response.use.mock.calls[0];

      const error403 = {
        response: {
          status: 403,
          headers: {},
        },
        config: { headers: { Authorization: "Bearer token" } },
      };

      Object.defineProperty(error403, Symbol.toStringTag, {
        value: "AxiosError",
      });

      const result = errorHandler(error403);

      expect(mockToast).not.toHaveBeenCalled();
      await expect(result).rejects.toBe(error403);
    });

    it("ignores errors that are not axios errors", async () => {
      httpInterceptorsModule.registerHttpInterceptors();
      const [, errorHandler] = mockApiClient.interceptors.response.use.mock.calls[0];

      const error = new Error("Generic error");

      const result = errorHandler(error);

      expect(mockToast).not.toHaveBeenCalled();
      await expect(result).rejects.toBe(error);
    });
  });

  describe("success responses", () => {
    it("passes through successful responses unchanged", () => {
      httpInterceptorsModule.registerHttpInterceptors();
      const [successHandler] = mockApiClient.interceptors.response.use.mock.calls[0];

      const response = { status: 200, data: { message: "OK" } };

      const result = successHandler(response);

      expect(result).toEqual(response);
    });
  });

  describe("registerSessionRefreshHandlers (S-53: warn instead of silently dropping the user)", () => {
    it("wires the auth store's refresh() as api-client's refresh handler", async () => {
      httpInterceptorsModule.registerSessionRefreshHandlers();

      expect(mockSetRefreshHandler).toHaveBeenCalledWith(expect.any(Function));
      const [registeredRefresh] = mockSetRefreshHandler.mock.calls[0];

      mockAuthRefresh.mockResolvedValue(true);
      await expect(registeredRefresh()).resolves.toBe(true);
      expect(mockAuthRefresh).toHaveBeenCalledTimes(1);
    });

    it("shows a session-expired toast when the refresh ultimately fails", () => {
      httpInterceptorsModule.registerSessionRefreshHandlers();

      expect(mockSetSessionExpiredHandler).toHaveBeenCalledWith(expect.any(Function));
      const [onSessionExpired] = mockSetSessionExpiredHandler.mock.calls[0];

      onSessionExpired();

      expect(mockToast).toHaveBeenCalledWith(
        expect.objectContaining({
          variant: "destructive",
          title: "common:errors.sessionExpiredTitle",
          description: "common:errors.sessionExpired",
        }),
      );
    });
  });
});
