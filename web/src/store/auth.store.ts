import axios from "axios";
import { create } from "zustand";
import { persist } from "zustand/middleware";
import { AUTH_TOKEN_STORAGE_KEY, apiClient } from "@senseik/api-client";
import { getMe, type MeResponse } from "@/services/me.service";
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

/** Mirrors `LoginResultDto` / `LoginStatuses` in Sense.Modules.Identity.Application/Dtos.cs. */
interface LoginResult {
  status: "ok" | "select_tenant" | "mfa_required";
  tokens?: {
    accessToken: string;
    refreshToken: string;
  };
}

// Base paths mirror `IdentityRoutes` in AuthController.cs.
const AUTH_BASE = "/v1/identity/auth";

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

interface AuthStore {
  user: User | null;
  token: string | null;
  refreshToken: string | null;
  /** `features` from /identity/me (effective access settings for the caller). */
  features: MeFeatures | null;
  isLoading: boolean;
  error: string | null;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
  /** Re-reads /identity/me (permissions, access settings) - call after changing anything that affects the caller. */
  refreshMe: () => Promise<void>;
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
      token: null,
      refreshToken: null,
      features: null,
      isLoading: false,
      error: null,

      login: async (email: string, password: string) => {
        set({ isLoading: true, error: null });
        try {
          const { data } = await apiClient.post<LoginResult>(`${AUTH_BASE}/login`, { email, password });

          // Tenant selection and MFA need their own screens, which don't exist yet.
          if (data.status !== "ok" || !data.tokens) {
            throw new Error(`Login requires an unsupported step: ${data.status}`);
          }

          // api-client attaches this key as the Bearer token, so /me (and every other service) is authenticated.
          localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, data.tokens.accessToken);
          const me = await getMe();

          set({
            token: data.tokens.accessToken,
            refreshToken: data.tokens.refreshToken,
            user: toUser(me),
            features: toFeatures(me),
            isLoading: false,
          });
        } catch (error) {
          localStorage.removeItem(AUTH_TOKEN_STORAGE_KEY);
          const message = toErrorMessage(error);
          set({ token: null, refreshToken: null, user: null, features: null, error: message, isLoading: false });
          throw new Error(message, { cause: error });
        }
      },

      logout: () => {
        localStorage.removeItem(AUTH_TOKEN_STORAGE_KEY);
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
        localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, token);
        set({ token, refreshToken });
      },

      isAuthenticated: () => {
        return !!get().token && !!get().user;
      },

      hasPermission: (permission) => {
        const user = get().user;
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
      partialize: (state) => ({
        user: state.user,
        token: state.token,
        refreshToken: state.refreshToken,
        features: state.features,
      }),
    }
  )
);
