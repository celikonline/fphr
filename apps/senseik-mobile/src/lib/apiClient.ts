import axios, { AxiosInstance, AxiosRequestConfig } from "axios";
import Constants from "expo-constants";
import { Platform } from "react-native";
import { SecureStorage } from "./storage";
import { currentLanguage } from "./i18n";

/** Header names mirror SenseHeaderNames on the API (Sense.Shared.Contracts.Security). */
const SenseHeaders = {
  CorrelationId: "X-Correlation-Id",
  ClientApp: "X-Client-App",
  ClientVersion: "X-Client-Version",
  ClientPlatform: "X-Client-Platform",
} as const;

const clientVersion = Constants.expoConfig?.version ?? "dev";

/** Hermes has no crypto.randomUUID; a v4-shaped random id is enough for log/trace correlation. */
const newCorrelationId = (): string =>
  "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
  });

// Paths mirror IdentityRoutes/AuthController.cs (Sense.Modules.Identity.Api): every anonymous
// endpoint under AUTH_BASE except `logout` (Bearer-authenticated, so a 401 there IS a real expired
// session and should still go through the refresh flow below).
const AUTH_BASE = "/api/v1/identity/auth";
const ANONYMOUS_AUTH_PATHS = [
  `${AUTH_BASE}/login`,
  `${AUTH_BASE}/mfa/complete`,
  `${AUTH_BASE}/refresh`,
  `${AUTH_BASE}/forgot-password`,
  `${AUTH_BASE}/reset-password`,
];

/**
 * B16: a 401 from an anonymous auth endpoint (wrong password, wrong TOTP code, ...) is NOT an
 * expired access token - the request never carried one. Treating it as one used to (a) try a token
 * refresh with whatever refresh token happened to be stored from a previous session, (b) silently
 * retry the SAME login/MFA request with a newly "refreshed" Authorization header if that succeeded,
 * and (c) log a scary "Login failed." / trigger `onUnauthorized` (session-expired logout) if it
 * didn't - none of which have anything to do with the user's actual wrong password. The same applies
 * to any request that never had an Authorization header in the first place (belt-and-suspenders for
 * other anonymous endpoints).
 */
function isAnonymousAuthRequest(config: { url?: string; headers?: unknown } | undefined): boolean {
  if (!config) return false;
  const url = config.url ?? "";
  if (ANONYMOUS_AUTH_PATHS.some((path) => url.includes(path))) return true;
  const headers = config.headers as Record<string, unknown> | undefined;
  return !headers?.Authorization;
}

interface QueuedRequest {
  resolve: (token?: string | null) => void;
  reject: (error: unknown) => void;
}

let isRefreshing = false;
let failedQueue: QueuedRequest[] = [];

/**
 * Fires when a token refresh attempt fails (refresh token missing/expired/rejected by the
 * server). The store registers itself here instead of this module importing the store directly,
 * to avoid a circular import (apiClient -> authStore -> AuthService -> apiClient).
 */
let onUnauthorized: (() => void) | null = null;

export const setUnauthorizedHandler = (handler: (() => void) | null): void => {
  onUnauthorized = handler;
};

const processQueue = (error: unknown, token?: string | null) => {
  failedQueue.forEach((prom) => {
    if (error) {
      prom.reject(error);
    } else {
      prom.resolve(token);
    }
  });

  isRefreshing = false;
  failedQueue = [];
};

/**
 * Never silently fall back to cleartext http: a release build with a missing
 * `EXPO_PUBLIC_API_BASE_URL` would otherwise send Bearer tokens unencrypted (MITM).
 */
function resolveBaseURL(): string {
  const configured = process.env.EXPO_PUBLIC_API_BASE_URL;
  if (configured) {
    if (__DEV__ && configured.startsWith("http://")) {
      const host = configured.split("/")[2]?.split(":")[0];
      if (host !== "localhost" && host !== "127.0.0.1" && host !== "10.0.2.2") {
        console.warn("[apiClient] API base URL uses cleartext http on a non-local host.");
      }
    }
    return configured;
  }
  if (!__DEV__) {
    throw new Error("EXPO_PUBLIC_API_BASE_URL is not configured for this release build.");
  }
  return "http://localhost:5000";
}

class ApiClient {
  private instance: AxiosInstance;
  private refreshInstance: AxiosInstance;

  constructor() {
    const baseURL = resolveBaseURL();
    this.instance = axios.create({
      baseURL,
      timeout: 30000,
      headers: {
        "Content-Type": "application/json",
      },
    });
    // Bare instance for the refresh call itself: it must not carry the expired
    // Authorization header and must not re-enter the 401 queue.
    this.refreshInstance = axios.create({
      baseURL,
      timeout: 30000,
      headers: {
        "Content-Type": "application/json",
      },
    });

    this.setupInterceptors();
  }

  private setupInterceptors(): void {
    // Request interceptor
    this.instance.interceptors.request.use(
      async (config) => {
        if (!config.headers.has(SenseHeaders.CorrelationId)) {
          config.headers.set(SenseHeaders.CorrelationId, newCorrelationId());
        }
        config.headers.set(SenseHeaders.ClientApp, "mobile");
        config.headers.set(SenseHeaders.ClientPlatform, Platform.OS);
        config.headers.set(SenseHeaders.ClientVersion, clientVersion);
        // FG-001: endpoints that localize server-side (e.g. `/leave/me/requests`'s `leaveTypeName`,
        // ProblemDetails error `detail`) answer in the app's active language, read per request so a
        // language switch applies without restarting.
        config.headers.set("Accept-Language", currentLanguage());

        const token = await SecureStorage.getAccessToken();
        if (token) {
          config.headers.Authorization = `Bearer ${token}`;
        }
        return config;
      },
      (error) => Promise.reject(error)
    );

    // Response interceptor
    this.instance.interceptors.response.use(
      (response) => response,
      async (error) => {
        const originalRequest = error.config;

        if (
          error.response?.status === 401 &&
          !originalRequest._retry &&
          !isAnonymousAuthRequest(originalRequest)
        ) {
          if (isRefreshing) {
            return new Promise((resolve, reject) => {
              failedQueue.push({ resolve, reject });
            })
              .then((token) => {
                originalRequest.headers.Authorization = `Bearer ${token}`;
                return this.instance(originalRequest);
              })
              .catch((error) => Promise.reject(error));
          }

          originalRequest._retry = true;
          isRefreshing = true;

          try {
            const refreshToken = await SecureStorage.getRefreshToken();
            if (!refreshToken) {
              throw new Error("No refresh token available");
            }

            const response = await this.refreshInstance.post(`${AUTH_BASE}/refresh`, {
              refreshToken,
            });

            // Validate the shape before trusting it - a malformed body must not
            // overwrite good tokens with `undefined` ("Bearer undefined" retries).
            // AuthTokensDto is returned unwrapped (no `data` envelope).
            const data = response.data;
            const accessToken: unknown = data?.accessToken;
            const newRefreshToken: unknown = data?.refreshToken;
            if (
              typeof accessToken !== "string" ||
              !accessToken ||
              typeof newRefreshToken !== "string" ||
              !newRefreshToken
            ) {
              throw new Error("Invalid refresh response");
            }

            await SecureStorage.setAccessToken(accessToken);
            await SecureStorage.setRefreshToken(newRefreshToken);

            originalRequest.headers.Authorization = `Bearer ${accessToken}`;
            processQueue(null, accessToken);

            return this.instance(originalRequest);
          } catch (err) {
            processQueue(err, null);
            // Clear tokens and drop the store's authenticated flag so the UI falls back to Login
            // instead of appearing signed in with a session the server has already rejected.
            await SecureStorage.clearAll();
            onUnauthorized?.();
            return Promise.reject(err);
          }
        }

        return Promise.reject(error);
      }
    );
  }

  get<T = unknown>(url: string, config?: AxiosRequestConfig) {
    return this.instance.get<T>(url, config);
  }

  post<T = unknown>(url: string, data?: unknown, config?: AxiosRequestConfig) {
    return this.instance.post<T>(url, data, config);
  }

  put<T = unknown>(url: string, data?: unknown, config?: AxiosRequestConfig) {
    return this.instance.put<T>(url, data, config);
  }

  patch<T = unknown>(url: string, data?: unknown, config?: AxiosRequestConfig) {
    return this.instance.patch<T>(url, data, config);
  }

  delete<T = unknown>(url: string, config?: AxiosRequestConfig) {
    return this.instance.delete<T>(url, config);
  }

  getBaseURL(): string {
    return this.instance.defaults.baseURL || "";
  }
}

export const apiClient = new ApiClient();
