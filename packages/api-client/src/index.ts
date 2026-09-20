/**
 * SenseİK API Client
 * Auto-generated from OpenAPI specification
 *
 * This file serves as the main entry point for the API client.
 * Generated code will be placed in ./generated/
 */

import axios, { AxiosError, AxiosInstance, AxiosResponse } from "axios";

declare module "axios" {
  interface AxiosRequestConfig {
    /** Set on the request the refresh handler itself makes, so its own 401 never re-enters the refresh flow (would recurse forever). */
    skipAuthRetry?: boolean;
    /** Set on the retried copy of a request after a successful token refresh, so a second 401 goes straight to session-expired instead of refreshing again. */
    _retriedAfterRefresh?: boolean;
  }
}

/**
 * Never fall back to an `http://` URL: a production build with a missing
 * `VITE_API_BASE_URL` would otherwise send the Bearer token in cleartext.
 * A same-origin `/api` fallback keeps dev (Vite proxy) working without that risk.
 */
const baseURL = import.meta.env.VITE_API_BASE_URL || "/api";

if (typeof window !== "undefined" && /^http:\/\//i.test(baseURL)) {
  const host = (() => {
    try {
      return new URL(baseURL).hostname;
    } catch {
      return "";
    }
  })();
  if (host !== "localhost" && host !== "127.0.0.1") {
    console.warn(
      "[api-client] VITE_API_BASE_URL uses cleartext http on a non-local host; tokens would travel unencrypted."
    );
  }
}

/** localStorage key holding the access token attached to every request. */
export const AUTH_TOKEN_STORAGE_KEY = "auth_token";

/**
 * sessionStorage key holding the web app's refresh token (tab-lifetime only, never localStorage - see
 * docs/adr/0009-web-refresh-token-storage.md). Kept here so `clearSession()` wipes it with the access token.
 */
export const REFRESH_TOKEN_STORAGE_KEY = "refresh_token";

/** The web app's persisted auth store (zustand `persist` name in web/src/store/auth.store.ts). */
const AUTH_STORE_STORAGE_KEY = "auth-store";

/** localStorage access must not throw (private mode / disabled storage). */
function readToken(): string | null {
  try {
    return localStorage.getItem(AUTH_TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

function clearSession(): void {
  try {
    localStorage.removeItem(AUTH_TOKEN_STORAGE_KEY);
    localStorage.removeItem(AUTH_STORE_STORAGE_KEY);
    sessionStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY);
  } catch {
    // Storage unavailable - nothing to clear.
  }
}

/**
 * Consumer-supplied token refresh: attempt to obtain a new access token (typically a call to
 * `/identity/auth/refresh` with the refresh token the consumer holds) and report whether it
 * succeeded. `api-client` only orchestrates *when* to call this and how to retry the failed
 * request - it has no knowledge of the auth store, since it's shared with the mobile app too.
 */
export type RefreshHandler = () => Promise<boolean>;
let refreshHandler: RefreshHandler | null = null;

/** Call once at app startup (see web/src/lib/http-interceptors.ts) to wire up token refresh. */
export function setRefreshHandler(handler: RefreshHandler | null): void {
  refreshHandler = handler;
}

/** Notified right before the hard redirect to /login, so the caller can warn the user first (S-53: silent kicks). */
export type SessionExpiredHandler = () => void;
let sessionExpiredHandler: SessionExpiredHandler | null = null;

export function setSessionExpiredHandler(handler: SessionExpiredHandler | null): void {
  sessionExpiredHandler = handler;
}

/** Several requests can 401 at once when the access token expires; only ever run one refresh call for them. */
let refreshInFlight: Promise<boolean> | null = null;

async function refreshOnce(): Promise<boolean> {
  if (!refreshHandler) return false;
  if (!refreshInFlight) {
    refreshInFlight = refreshHandler()
      .then((ok) => ok)
      .catch(() => false)
      .finally(() => {
        refreshInFlight = null;
      });
  }
  return refreshInFlight;
}

/**
 * Ends the session exactly once even if several in-flight requests 401 together (each would
 * otherwise redirect/clear/notify independently). `window.location.assign` triggers a full page
 * load, so this module - and the flag below - is re-initialized fresh on the next page anyway.
 */
let handlingSessionExpiry = false;

function handleSessionExpired(): void {
  if (handlingSessionExpiry) return;
  handlingSessionExpiry = true;
  // Clear both the token and the persisted store, otherwise the route guard still sees a
  // logged-in user after the redirect.
  clearSession();
  sessionExpiredHandler?.();
  // Fixed same-origin path - never a caller-supplied URL, so no open redirect.
  window.location.assign("/login");
}

const apiClient: AxiosInstance = axios.create({
  baseURL,
  timeout: 30_000,
  headers: {
    "Content-Type": "application/json",
  },
});

/** Header names mirror SenseHeaderNames on the API (Sense.Shared.Contracts.Security). */
export const SenseHeaders = {
  CorrelationId: "X-Correlation-Id",
  RequestId: "X-Request-Id",
  TraceId: "X-Trace-Id",
  ClientApp: "X-Client-App",
  ClientVersion: "X-Client-Version",
  ClientPlatform: "X-Client-Platform",
} as const;

const clientVersion: string = import.meta.env.VITE_APP_VERSION || "dev";

/** crypto.randomUUID is only available in secure contexts (https/localhost); fall back elsewhere. */
function newCorrelationId(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === "x" ? r : (r & 0x3) | 0x8).toString(16);
  });
}

// Single request interceptor (tracing/client headers + auth token): one place to audit,
// no ordering surprises between chained interceptors.
apiClient.interceptors.request.use((config) => {
  // Tracing/client headers: every request carries its own correlation id (unless the caller
  // set one to tie several calls together) so a failed call can be found in Grafana/Jaeger
  // by the id shown to the user.
  if (!config.headers.has(SenseHeaders.CorrelationId)) {
    config.headers.set(SenseHeaders.CorrelationId, newCorrelationId());
  }
  config.headers.set(SenseHeaders.ClientApp, "web");
  config.headers.set(SenseHeaders.ClientPlatform, "web");
  config.headers.set(SenseHeaders.ClientVersion, clientVersion);
  // Bearer token is never sent via query params - header only.
  const token = readToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

/**
 * Response error handler: on a 401 from an authenticated request, try a single token refresh
 * (deduped across concurrent 401s) and retry the original request once; only end the session if
 * the refresh itself fails or no refresh handler is registered. Exported (rather than inlined in
 * `.use(...)`) so it can be unit-tested directly without reaching into axios interceptor internals.
 */
export async function handleResponseError(error: AxiosError): Promise<AxiosResponse> {
  const config = error.config;
  // Only an authenticated request's 401 means the session expired/needs a refresh. A 401 from an
  // anonymous request (e.g. wrong password on login) must reach the caller as-is.
  const hadAuthHeader = !!config?.headers?.Authorization;
  if (error.response?.status !== 401 || !hadAuthHeader || !config) {
    return Promise.reject(error);
  }

  // The refresh call itself is marked `skipAuthRetry` by its caller - its own 401 (invalid/expired
  // refresh token) must fall straight through to session-expired, not attempt another refresh.
  if (!config.skipAuthRetry && !config._retriedAfterRefresh) {
    const refreshed = await refreshOnce();
    if (refreshed) {
      // Re-issuing through `apiClient.request` runs the request interceptor again, which reads the
      // now-refreshed token from storage and sets a fresh Authorization header - no need to do it here.
      return apiClient.request({ ...config, _retriedAfterRefresh: true });
    }
  }

  handleSessionExpired();
  return Promise.reject(error);
}

apiClient.interceptors.response.use((response) => response, handleResponseError);

export { apiClient };
export default apiClient;

// TODO: Import and export generated API clients
// export * from './generated/...';
