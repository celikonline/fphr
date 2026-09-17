import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import axios from "axios";
import * as httpInterceptorsModule from "./http-interceptors";

// Mock axios
vi.mock("axios", async () => {
  const actual = await vi.importActual("axios");
  return {
    default: {
      ...actual,
      isAxiosError: (error: unknown) => {
        if (error instanceof Error && "response" in error && "config" in error) {
          return true;
        }
        return false;
      },
    },
  };
});

// Mock @senseik/api-client
const mockApiClient = {
  interceptors: {
    response: {
      use: vi.fn(),
    },
  },
};

vi.mock("@senseik/api-client", () => ({
  apiClient: mockApiClient,
}));

// Mock toast hook
const mockToast = vi.fn();
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
  beforeEach(() => {
    vi.clearAllMocks();
    // Reset module-level state (lastToastAt)
    vi.resetModules();
  });

  afterEach(() => {
    vi.clearAllTimers();
  });

  it("registers response interceptor on apiClient", () => {
    httpInterceptorsModule.registerHttpInterceptors();
    expect(mockApiClient.interceptors.response.use).toHaveBeenCalled();
  });

  describe("429 rate limit handling", () => {
    it("shows toast on 429 response with Retry-After header", () => {
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
      expect(result).toEqual(Promise.reject(error));
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
      errorHandler(error);
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
      errorHandler(error);
      expect(mockToast).toHaveBeenCalledTimes(1);

      // Second error immediately after: toast should NOT show (throttled)
      errorHandler(error);
      expect(mockToast).toHaveBeenCalledTimes(1);

      // Advance time past TOAST_THROTTLE_MS (4000 ms)
      vi.advanceTimersByTime(4001);

      // Third error: toast should show again
      errorHandler(error);
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

      errorHandler(error);

      expect(mockToast).toHaveBeenCalledWith({
        variant: "destructive",
        description: expect.stringMatching(/4[0-9]|45/), // ~45 seconds
      });
    });

    it("ignores non-429 errors", () => {
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
      expect(result).toEqual(Promise.reject(error403));
    });

    it("ignores errors that are not axios errors", () => {
      httpInterceptorsModule.registerHttpInterceptors();
      const [, errorHandler] = mockApiClient.interceptors.response.use.mock.calls[0];

      const error = new Error("Generic error");

      const result = errorHandler(error);

      expect(mockToast).not.toHaveBeenCalled();
      expect(result).toEqual(Promise.reject(error));
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
});
