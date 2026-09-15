/**
 * SenseİK API Client
 * Auto-generated from OpenAPI specification
 *
 * This file serves as the main entry point for the API client.
 * Generated code will be placed in ./generated/
 */

import axios, { AxiosInstance } from "axios";

const baseURL = import.meta.env.VITE_API_BASE_URL || "http://localhost:5000/api";

/** localStorage key holding the access token attached to every request. */
export const AUTH_TOKEN_STORAGE_KEY = "auth_token";

/** The web app's persisted auth store (zustand `persist` name in web/src/store/auth.store.ts). */
const AUTH_STORE_STORAGE_KEY = "auth-store";

const apiClient: AxiosInstance = axios.create({
  baseURL,
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

// Tracing/client headers: every request carries its own correlation id (unless the caller set one to tie
// several calls together) so a failed call can be found in Grafana/Jaeger by the id shown to the user.
apiClient.interceptors.request.use((config) => {
  if (!config.headers.has(SenseHeaders.CorrelationId)) {
    config.headers.set(SenseHeaders.CorrelationId, newCorrelationId());
  }
  config.headers.set(SenseHeaders.ClientApp, "web");
  config.headers.set(SenseHeaders.ClientPlatform, "web");
  config.headers.set(SenseHeaders.ClientVersion, clientVersion);
  return config;
});

// Add interceptor to include auth token
apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem(AUTH_TOKEN_STORAGE_KEY);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Add response interceptor for error handling
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    // Only an authenticated request's 401 means the session expired. A 401 from an anonymous
    // request (e.g. wrong password on login) must reach the caller, not reload the page and
    // swallow the error message.
    if (error.response?.status === 401 && error.config?.headers?.Authorization) {
      // Clear both the token and the persisted store, otherwise the route guard still sees a
      // logged-in user after the redirect.
      localStorage.removeItem(AUTH_TOKEN_STORAGE_KEY);
      localStorage.removeItem(AUTH_STORE_STORAGE_KEY);
      window.location.href = "/login";
    }
    return Promise.reject(error);
  }
);

export { apiClient };
export default apiClient;

// TODO: Import and export generated API clients
// export * from './generated/...';
