import axios from "axios";
import { create } from "zustand";
import { persist } from "zustand/middleware";
import { AUTH_TOKEN_STORAGE_KEY, REFRESH_TOKEN_STORAGE_KEY, apiClient } from "@senseik/api-client";
import { getMe, type MeResponse } from "@/services/me.service";
import { getImpersonatorId } from "@/lib/jwt";
import type { AccessSettingKey, AccessSettings, MeFeatures } from "@/types";

export interface User {
  id: string;
  email: string;
  name: string;
  roles: string[];
  /** Display labels for `roles` (same order/length) - backend-localized (RolesController's "roles.{code}.name"
   * resource keys, faz1a senseik-040), so this always matches what Kullanıcılar → Roller shows for the same role. */
  roleLabels: string[];
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

/*
 * Refresh token storage (docs/adr/0009-web-refresh-token-storage.md).
 * The refresh token lives in sessionStorage ONLY: it survives a full page reload (F5) but is dropped when the
 * tab/browser closes, so a reload no longer kicks the user to /login once the 15-min access token expires.
 * Never move it to localStorage or zustand `persist`: it is long-lived and rotated, so any XSS payload could
 * exfiltrate it and outlive the tab. Accepted residual risk: script running in this origin can read
 * sessionStorage during the session (same exposure as the access token); the proper fix is an httpOnly cookie
 * (ADR "Sonraki adım"). A tab duplicated/forked from this one carries a copy that goes stale after a rotation.
 */
function readStoredRefreshToken(): string | null {
  try {
    return sessionStorage.getItem(REFRESH_TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

function writeStoredRefreshToken(refreshToken: string | null | undefined): void {
  try {
    if (refreshToken) {
      sessionStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, refreshToken);
    } else {
      sessionStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY);
    }
  } catch {
    // Refresh token stays in memory only: survives navigation, not a reload.
  }
}

/** Drops every client-side copy of the session credentials (access token, refresh token). */
function clearStoredCredentials(): void {
  safeStorageRemove(AUTH_TOKEN_STORAGE_KEY);
  writeStoredRefreshToken(null);
}

/**
 * Best-effort server-side revocation on logout, so a copied refresh token is worthless. Never throws and
 * never triggers api-client's session-expired redirect (`skipAuthRetry` + skipped when the access token is
 * already expired, since a 401 here would otherwise toast "session expired" right after the user chose to leave).
 */
async function revokeRefreshToken(accessToken: string | null, refreshToken: string | null): Promise<void> {
  if (!accessToken || !refreshToken || isJwtExpired(accessToken)) return;
  try {
    await apiClient.post(
      `${AUTH_BASE}/logout`,
      { refreshToken, allSessions: false },
      // Explicit header: the request interceptor reads localStorage, which logout() has already cleared.
      { headers: { Authorization: `Bearer ${accessToken}` }, skipAuthRetry: true },
    );
  } catch {
    // The server-side token simply expires on its own; the local session is already gone.
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

/** Exported for unit testing (senseik-040): maps the backend-localized `/me` roles onto the store's `User` shape. */
export function toUser(me: MeResponse): User {
  return {
    id: me.userId,
    email: me.email,
    name: `${me.firstName} ${me.lastName}`.trim(),
    roles: me.roles.map((role) => role.code),
    roleLabels: me.roles.map((role) => role.name ?? role.code),
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
    mfaSetupRequired: me.features?.mfaSetupRequired ?? false,
    disabledModules: me.features?.disabledModules ?? [],
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
  writeStoredRefreshToken(tokens.refreshToken);
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
  /**
   * False until zustand's `persist` middleware has restored `user`/`features` from storage.
   * `token` is read synchronously (see below), but `user`/`features` only exist after this
   * rehydration completes - it runs a tick after the store is created, not before the first
   * render. Reading `hasPermission`/`isAccessSettingEnabled` before this flips true always
   * sees an empty profile, which is why permission-gated buttons ("+ Talep", "Yeni çalışan
   * ekle", "Düzenle", ...) could silently be absent from the DOM on the very first paint after
   * a page load - a click aimed at where the button will be lands on nothing, and only a retry
   * after hydration finishes appears to "work" (senseik-042, çift-tıklama kök nedeni).
   * `ProtectedRoute` waits on this before rendering the authenticated shell.
   */
  hasHydrated: boolean;
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
  /** senseik-057: false when the tenant turned this module off (Ayarlar → Modüller). Core/unknown keys are always on. */
  isModuleEnabled: (key: string) => boolean;
}

// `persist`'s `onRehydrateStorage` callback below can run *synchronously*, inside `create()` itself,
// when storage (localStorage) resolves synchronously - i.e. before `useAuthStore` below finishes
// being assigned. Closing over `useAuthStore` there throws "Cannot access before initialization"
// (silently swallowed by persist's internal thenable, so `hasHydrated` would never flip). Capture
// the store's own `set` from the creator instead - it is available the moment the creator runs,
// which always happens before hydration.
let setAuthState: ((partial: Partial<AuthStore>) => void) | null = null;
let getAuthState: (() => AuthStore) | null = null;

export const useAuthStore = create<AuthStore>()(
  persist(
    (set, get) => {
      setAuthState = set;
      getAuthState = get;
      return {
        user: null,
        token: typeof window === "undefined" ? null : readStoredToken(),
        // Read synchronously (like `token`) so a refresh right after F5 finds it - see readStoredRefreshToken().
        refreshToken: typeof window === "undefined" ? null : readStoredRefreshToken(),
        features: null,
        isLoading: false,
        error: null,
        hasHydrated: false,

        login: async (email: string, password: string, tenantSlug?: string) => {
          set({ isLoading: true, error: null });
          try {
            const { data } = await apiClient.post<LoginResult>(`${AUTH_BASE}/login`, {
              email,
              password,
              tenantSlug,
            });

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
            clearStoredCredentials();
            const message = toErrorMessage(error);
            set({
              token: null,
              refreshToken: null,
              user: null,
              features: null,
              error: message,
              isLoading: false,
            });
            throw new Error(message, { cause: error });
          }
        },

        completeMfaLogin: async (mfaToken: string, code: string) => {
          set({ isLoading: true, error: null });
          try {
            const { data } = await apiClient.post<LoginResult>(`${AUTH_BASE}/mfa/complete`, {
              mfaToken,
              code,
            });
            if (data.status !== "ok" || !data.tokens) {
              throw new Error(`MFA completion requires an unsupported step: ${data.status}`);
            }
            await establishSession(set, data.tokens);
            return { status: "ok" };
          } catch (error) {
            clearStoredCredentials();
            const message = toErrorMessage(error);
            set({
              token: null,
              refreshToken: null,
              user: null,
              features: null,
              error: message,
              isLoading: false,
            });
            throw new Error(message, { cause: error });
          }
        },

        logout: () => {
          const { token, refreshToken } = get();
          clearStoredCredentials();
          void revokeRefreshToken(token, refreshToken);
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

        setUser: (user) => set({ user }),

        setToken: (token, refreshToken) => {
          safeStorageSet(AUTH_TOKEN_STORAGE_KEY, token);
          writeStoredRefreshToken(refreshToken);
          set({ token, refreshToken });
        },

        isAuthenticated: () => {
          const { token, user } = get();
          // An expired JWT must not count as authenticated, otherwise the guard renders
          // protected pages until the first API 401 fires.
          return !!token && !!user && !isJwtExpired(token);
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
              { skipAuthRetry: true },
            );
            safeStorageSet(AUTH_TOKEN_STORAGE_KEY, data.accessToken);
            writeStoredRefreshToken(data.refreshToken);
            set({ token: data.accessToken, refreshToken: data.refreshToken });
            return true;
          } catch {
            return false;
          }
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

        isModuleEnabled: (key) => {
          // Missing features (not loaded yet / older persisted state) = nothing disabled; the server still enforces.
          return !(get().features?.disabledModules ?? []).includes(key);
        },
      };
    },
    {
      name: "auth-store",
      version: 2,
      // Tokens are intentionally NOT part of the zustand-persisted blob (localStorage): the access token
      // has its own api-client key and the refresh token lives in sessionStorage (tab-lifetime only),
      // so no long-lived copy survives the browser closing - see readStoredRefreshToken() and
      // docs/adr/0009-web-refresh-token-storage.md. (Full mitigation is an httpOnly cookie - backend change.)
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
      // Runs once storage has been read, whether or not there was anything to restore (including
      // storage being unavailable) - `hasHydrated` must flip in every case or the app would hang
      // on the loading screen in private-browsing mode.
      // After a reload the access token may already have expired (F5 after >15 min, restored tab): when a
      // refresh token survived in sessionStorage, rotate it BEFORE flipping `hasHydrated`, otherwise
      // ProtectedRoute would treat the expired token as "logged out" and send the user to /login.
      onRehydrateStorage: () => (hydrated) => {
        // `get()` can still be empty when this runs synchronously inside `create()`; fall back to the callback's state.
        const state = getAuthState?.() ?? hydrated;
        const needsRefresh = !!state?.refreshToken && (!state.token || isJwtExpired(state.token));
        if (!state || !needsRefresh) {
          setAuthState?.({ hasHydrated: true });
          return;
        }
        void state.refresh().finally(() => setAuthState?.({ hasHydrated: true }));
      },
    }
  )
);
