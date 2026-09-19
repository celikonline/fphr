import axios from "axios";
import { create } from "zustand";
import { persist } from "zustand/middleware";
import { AUTH_TOKEN_STORAGE_KEY, apiClient } from "@senseik/api-client";
import { getMe, type MeResponse } from "@/services/me.service";
import { getImpersonatorId } from "@/lib/jwt";
import type { AccessSettingKey, AccessSettings, MeFeatures } from "@/types";

export interface User {
  id: string;
  email: string;
  name: string;
  roles: string[];
  permissions: string[];
  firstName?: string;
  lastName?: string;
  employeeId?: string | null;
  preferredLanguage?: string;
  mfaEnabled?: boolean;
  tenant?: { id: string; name: string; slug: string } | null;
}

export interface TenantChoice {
  tenantId: string;
  slug: string;
  name: string;
}

/** Mirrors `LoginResultDto` / `LoginStatuses` in Sense.Modules.Identity.Application/Dtos.cs. */
interface LoginResult {
  status: "ok" | "select_tenant" | "mfa_required";
  tokens?: {
    accessToken: string;
    refreshToken: string;
  };
  tenants?: TenantChoice[];
  /** Present when `status === "mfa_required"`; pass back to `auth/mfa/complete` with the TOTP code (S-5, tur 3). */
  mfaToken?: string;
}

/** Outcome of `login()`/`completeMfaLogin()`: session established, tenant pick required, or MFA code required. */
export type LoginOutcome =
  | { status: "ok" }
  | { status: "select_tenant"; tenants: TenantChoice[] }
  | { status: "mfa_required"; mfaToken: string };

// Base paths mirror `IdentityRoutes` in AuthController.cs.
const AUTH_BASE = "/v1/identity/auth";

/** Mirrors `AuthTokensDto` in Sense.Modules.Identity.Application/Dtos.cs. */
interface AuthTokensResponse {
  accessToken: string;
  refreshToken: string;
}

const EMPTY_ACCESS: AccessSettings = {
  showOrgInfo: false,
  showUpcomingLeaves: false,
  showUpcomingBirthdays: false,
  showEventCalendar: false,
  showFiles: false,
  showAssets: false,
  showWorkPhoneNumbers: false,
  showOrgChart: false,
  showCustomFields: false,
  showCalendarMenu: false,
  showAllLeavesInCalendar: false,
};

/** localStorage may throw (private mode); auth must keep working without it. */
function safeStorageSet(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Token stays in memory only for this tab.
  }
}

function safeStorageRemove(key: string): void {
  try {
    localStorage.removeItem(key);
  } catch {
    // Nothing to clear.
  }
}

/** Single live copy of the api-client token (session survives reload; the zustand persist no longer keeps a second copy). */
function readStoredToken(): string | null {
  try {
    return localStorage.getItem(AUTH_TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

/** True when the JWT `exp` claim is in the past (or the token is unreadable). */
function isJwtExpired(token: string): boolean {
  try {
    const payload = JSON.parse(atob(token.split(".")[1] ?? ""));
    return typeof payload.exp !== "number" || payload.exp * 1000 <= Date.now();
  } catch {
    return true;
  }
}

/** Backend errors are ProblemDetails; prefer its localized `detail` over axios' generic message. */
function toErrorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const detail = (error.response?.data as { detail?: string } | undefined)?.detail;
    if (detail) {
      return detail;
    }
  }
  return error instanceof Error ? error.message : "Login failed";
}

function toUser(me: MeResponse): User {
  return {
    id: me.userId,
    email: me.email,
    name: `${me.firstName} ${me.lastName}`.trim(),
    roles: me.roles.map((role) => role.code),
    permissions: me.permissions,
    firstName: me.firstName,
    lastName: me.lastName,
    employeeId: me.employeeId,
    preferredLanguage: me.preferredLanguage,
    mfaEnabled: me.mfaEnabled,
    tenant: me.tenant ? { id: me.tenant.id, name: me.tenant.name, slug: me.tenant.slug } : null,
  };
}

function toFeatures(me: MeResponse): MeFeatures {
  return {
    access: { ...EMPTY_ACCESS, ...(me.features?.access ?? {}) },
    accessSettingsBypass: me.features?.accessSettingsBypass ?? false,
  };
}

/**
 * Shared by `login()` (status "ok") and `completeMfaLogin()` (status "ok" after the TOTP step): stores the
 * tokens, loads `/me`, and populates the store. Both call sites already validated `tokens` is present.
 */
async function establishSession(
  set: (partial: Partial<AuthStore>) => void,
  tokens: { accessToken: string; refreshToken: string }
): Promise<void> {
  // api-client attaches this key as the Bearer token, so /me (and every other service) is authenticated.
  safeStorageSet(AUTH_TOKEN_STORAGE_KEY, tokens.accessToken);
  const me = await getMe();

  set({
    token: tokens.accessToken,
    refreshToken: tokens.refreshToken,
    user: toUser(me),
    features: toFeatures(me),
    isLoading: false,
  });
}

interface AuthStore {
  user: User | null;
  token: string | null;
  refreshToken: string | null;
  /** `features` from /identity/me (effective access settings for the caller). */
  features: MeFeatures | null;
  isLoading: boolean;
  error: string | null;
  login: (email: string, password: string, tenantSlug?: string) => Promise<LoginOutcome>;
  /** Second step of a login that returned `mfa_required`: TOTP code + the `mfaToken` from that response. */
  completeMfaLogin: (mfaToken: string, code: string) => Promise<LoginOutcome>;
  logout: () => void;
  /** Re-reads /identity/me (permissions, access settings) - call after changing anything that affects the caller. */
  refreshMe: () => Promise<void>;
  /**
   * Rotates the access/refresh token pair via `/identity/auth/refresh`. Wired into api-client's
   * `setRefreshHandler` (see web/src/lib/http-interceptors.ts) so a 401 refreshes silently instead
   * of immediately logging the user out. Returns false (never throws) on any failure - the caller
   * (api-client) falls back to ending the session.
   */
  refresh: () => Promise<boolean>;
  setUser: (user: User) => void;
  setToken: (token: string, refreshToken?: string) => void;
  isAuthenticated: () => boolean;
  hasPermission: (permission: string) => boolean;
  hasRole: (role: string) => boolean;
  isAccessSettingEnabled: (key: AccessSettingKey) => boolean;
}

export const useAuthStore = create<AuthStore>()(
  persist(
    (set, get) => ({
      user: null,
      token: typeof window === "undefined" ? null : readStoredToken(),
      refreshToken: null,
      features: null,
      isLoading: false,
      error: null,

      login: async (email: string, password: string, tenantSlug?: string) => {
        set({ isLoading: true, error: null });
        try {
          const { data } = await apiClient.post<LoginResult>(`${AUTH_BASE}/login`, { email, password, tenantSlug });

          if (data.status === "select_tenant") {
            set({ isLoading: false });
            return { status: "select_tenant", tenants: data.tenants ?? [] };
          }

          if (data.status === "mfa_required") {
            set({ isLoading: false });
            return { status: "mfa_required", mfaToken: data.mfaToken ?? "" };
          }

          if (data.status !== "ok" || !data.tokens) {
            throw new Error(`Login requires an unsupported step: ${data.status}`);
          }

          await establishSession(set, data.tokens);
          return { status: "ok" };
        } catch (error) {
          safeStorageRemove(AUTH_TOKEN_STORAGE_KEY);
          const message = toErrorMessage(error);
          set({ token: null, refreshToken: null, user: null, features: null, error: message, isLoading: false });
          throw new Error(message, { cause: error });
        }
      },

      completeMfaLogin: async (mfaToken: string, code: string) => {
        set({ isLoading: true, error: null });
        try {
          const { data } = await apiClient.post<LoginResult>(`${AUTH_BASE}/mfa/complete`, { mfaToken, code });
          if (data.status !== "ok" || !data.tokens) {
            throw new Error(`MFA completion requires an unsupported step: ${data.status}`);
          }
          await establishSession(set, data.tokens);
          return { status: "ok" };
        } catch (error) {
          safeStorageRemove(AUTH_TOKEN_STORAGE_KEY);
          const message = toErrorMessage(error);
          set({ token: null, refreshToken: null, user: null, features: null, error: message, isLoading: false });
          throw new Error(message, { cause: error });
        }
      },

      logout: () => {
        safeStorageRemove(AUTH_TOKEN_STORAGE_KEY);
        set({ user: null, token: null, refreshToken: null, features: null, error: null });
      },

      refreshMe: async () => {
        if (!get().token) return;
        try {
          const me = await getMe();
          set({ user: toUser(me), features: toFeatures(me) });
        } catch {
          // A 401 is handled by the api-client interceptor; other failures keep the cached profile.
        }
      },

      refresh: async () => {
        const refreshToken = get().refreshToken;
        if (!refreshToken) return false;
        try {
          // `skipAuthRetry` tells api-client's response interceptor not to treat THIS call's own
          // 401 (an invalid/expired refresh token) as another session to refresh - it would recurse.
          const { data } = await apiClient.post<AuthTokensResponse>(
            `${AUTH_BASE}/refresh`,
            { refreshToken },
            { skipAuthRetry: true }
          );
          safeStorageSet(AUTH_TOKEN_STORAGE_KEY, data.accessToken);
          set({ token: data.accessToken, refreshToken: data.refreshToken });
          return true;
        } catch {
          return false;
        }
      },

      setUser: (user) => set({ user }),

      setToken: (token, refreshToken) => {
        safeStorageSet(AUTH_TOKEN_STORAGE_KEY, token);
        set({ token, refreshToken });
      },

      isAuthenticated: () => {
        const { token, user } = get();
        // An expired JWT must not count as authenticated, otherwise the guard renders
        // protected pages until the first API 401 fires.
        return !!token && !!user && !isJwtExpired(token);
      },

      hasPermission: (permission) => {
        const { user, token } = get();
        // Mirrors AuthorizationBehaviour's own rule: a platform user's active impersonation session skips
        // every [RequiresPermission] check server-side, so the UI must not hide things it can already do
        // (faz0-durum-takibi.md #11 - `/identity/me` returns an empty permission list during impersonation).
        if (getImpersonatorId(token)) return true;
        return user?.permissions.includes(permission) ?? false;
      },

      hasRole: (role) => {
        const user = get().user;
        return user?.roles.includes(role) ?? false;
      },

      isAccessSettingEnabled: (key) => {
        const features = get().features;
        if (!features) return false;
        return features.accessSettingsBypass || features.access[key] === true;
      },
    }),
    {
      name: "auth-store",
      version: 2,
      // Tokens are intentionally NOT persisted: a single copy lives in memory plus the
      // api-client key for the current tab. Persisting them would keep a second, long-lived
      // copy in localStorage that any XSS payload could exfiltrate after the tab closes.
      // (Full mitigation is httpOnly cookies + refresh rotation - a backend change.)
      partialize: (state) => ({
        user: state.user,
        features: state.features,
      }),
      migrate: (persistedState, _version) => {
        // Drop tokens persisted by older versions (single live copy only from now on).
        if (persistedState && typeof persistedState === "object") {
          const state = persistedState as Record<string, unknown>;
          delete state.token;
          delete state.refreshToken;
        }
        return persistedState;
      },
    }
  )
);
