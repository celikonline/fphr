/**
 * Web-only `apiClient` response interceptors.
 *
 * `@senseik/api-client` is a shared package also consumed by the Expo mobile app (see
 * apps/senseik-mobile/package.json), so anything web-specific (Mantine toasts, i18next) has to be
 * layered on here rather than added to that package. Axios lets multiple callers register
 * interceptors on the same instance, so this composes with the 401 handler already registered in
 * packages/api-client/src/index.ts - it does not replace it.
 *
 * ADR-0003 (rate limiting, Faz 1 BE): shows a localized toast on HTTP 429 with the `Retry-After`
 * wait time. No automatic retry - the caller still gets the rejected promise/error as before.
 */
import axios from "axios";
import { apiClient, setRefreshHandler, setSessionExpiredHandler } from "@senseik/api-client";
import i18n from "@/i18n";
import { toast } from "@/hooks/use-toast";
import { useAuthStore } from "@/store/auth.store";

/** Falls back to the shortest configured rate-limit window (see appsettings.json RateLimiting) when the response has no usable `Retry-After`. */
const DEFAULT_RETRY_AFTER_SECONDS = 60;
/** Avoids a toast storm when several in-flight requests all come back 429 at once. */
const TOAST_THROTTLE_MS = 4000;

let lastToastAt = 0;

function parseRetryAfterSeconds(header: unknown): number | undefined {
  if (typeof header !== "string" || header.trim() === "") return undefined;
  const seconds = Number(header);
  if (Number.isFinite(seconds)) return Math.max(0, Math.round(seconds));
  const dateMs = Date.parse(header);
  if (!Number.isNaN(dateMs)) return Math.max(0, Math.round((dateMs - Date.now()) / 1000));
  return undefined;
}

export function registerHttpInterceptors(): void {
  apiClient.interceptors.response.use(
    (response) => response,
    (error) => {
      if (axios.isAxiosError(error) && error.response?.status === 429) {
        const now = Date.now();
        if (now - lastToastAt > TOAST_THROTTLE_MS) {
          lastToastAt = now;
          const seconds = parseRetryAfterSeconds(error.response.headers?.["retry-after"]) ?? DEFAULT_RETRY_AFTER_SECONDS;
          toast({
            variant: "destructive",
            description: i18n.t("common:errors.rateLimited", { seconds }),
          });
        }
      }
      return Promise.reject(error);
    }
  );
}

/**
 * Wires api-client's generic refresh/session-expiry hooks to the web auth store and a toast
 * (S-53: users were silently dropped to /login every ~15-30 min when the access token expired,
 * because no refresh was ever attempted - see api-client's `handleResponseError`). Call once at
 * startup alongside `registerHttpInterceptors()`.
 */
export function registerSessionRefreshHandlers(): void {
  setRefreshHandler(() => useAuthStore.getState().refresh());
  setSessionExpiredHandler(() => {
    toast({
      variant: "destructive",
      title: i18n.t("common:errors.sessionExpiredTitle"),
      description: i18n.t("common:errors.sessionExpired"),
      duration: false,
    });
  });
}
