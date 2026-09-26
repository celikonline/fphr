import { apiClient } from "@/lib/apiClient";
import { SecureStorage, PreferenceStorage } from "@/lib/storage";
import { biometricService } from "@/lib/biometric";
import { User, AuthTokens, TenantChoice } from "@/types";

// Paths mirror IdentityRoutes / AuthController.cs and MeController.cs (Sense.Modules.Identity.Api).
const AUTH_BASE = "/api/v1/identity/auth";
const ME_PATH = "/api/v1/identity/me";

/** Wire shape of `AuthTokensDto`. */
interface AuthTokensResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  tokenType: string;
}

/** Wire shape of `LoginResultDto`. */
interface LoginResultResponse {
  status: "ok" | "select_tenant" | "mfa_required";
  tokens?: AuthTokensResponse;
  tenants?: TenantChoice[];
  mfaToken?: string;
}

/** Wire shape of `MeDto` (only the fields the mobile `User` type needs). */
interface MeResponse {
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  employeeId?: string;
  preferredLanguage: string;
  mfaEnabled: boolean;
  tenant: { id: string; name: string; slug: string };
  roles: Array<{ id: string; code: string; name?: string }>;
  permissions: string[];
}

/** Outcome of `login()`: session established, or the caller must pick a tenant and call `login` again with `tenantSlug`. */
export type LoginOutcome =
  | { status: "ok"; user: User; tokens: AuthTokens }
  | { status: "select_tenant"; tenants: TenantChoice[] };

const MFA_NOT_SUPPORTED_MESSAGE =
  "İki adımlı doğrulama mobil uygulamada henüz desteklenmiyor. Lütfen web tarayıcısından giriş yapın.";

function toAuthTokens(tokens: AuthTokensResponse): AuthTokens {
  return {
    accessToken: tokens.accessToken,
    accessTokenExpiresAt: tokens.accessTokenExpiresAt,
    refreshToken: tokens.refreshToken,
    refreshTokenExpiresAt: tokens.refreshTokenExpiresAt,
    tokenType: tokens.tokenType,
  };
}

function toUser(me: MeResponse): User {
  return {
    id: me.userId,
    tenantId: me.tenant.id,
    email: me.email,
    firstName: me.firstName,
    lastName: me.lastName,
    employeeId: me.employeeId ?? null,
    preferredLanguage: me.preferredLanguage,
    mfaEnabled: me.mfaEnabled,
    roles: me.roles.map((role) => role.code),
    permissions: me.permissions,
    tenant: me.tenant,
  };
}

export class AuthService {
  /**
   * `tenantSlug` is passed when the caller already resolved a `select_tenant` choice from a
   * previous call - the backend then returns `status: "ok"` for that tenant.
   */
  async login(email: string, password: string, tenantSlug?: string): Promise<LoginOutcome> {
    try {
      const response = await apiClient.post<LoginResultResponse>(`${AUTH_BASE}/login`, {
        email,
        password,
        tenantSlug,
      });
      const result = response.data;

      if (result.status === "select_tenant") {
        return { status: "select_tenant", tenants: result.tenants ?? [] };
      }

      if (result.status === "mfa_required") {
        throw new Error(MFA_NOT_SUPPORTED_MESSAGE);
      }

      if (result.status !== "ok" || !result.tokens) {
        throw new Error("Beklenmeyen giriş yanıtı.");
      }

      const tokens = toAuthTokens(result.tokens);
      await SecureStorage.setAccessToken(tokens.accessToken);
      await SecureStorage.setRefreshToken(tokens.refreshToken);

      const user = await this.fetchAndStoreCurrentUser();
      if (!user) {
        throw new Error("Kullanıcı bilgileri alınamadı.");
      }

      await PreferenceStorage.setLastLoginEmail(email);

      return { status: "ok", user, tokens };
    } catch (error) {
      // Never log the error object: axios errors embed the request password and tokens.
      if (__DEV__) {
        console.error("Login failed.");
      }
      throw error;
    }
  }

  async logout(): Promise<void> {
    try {
      const refreshToken = await SecureStorage.getRefreshToken();
      // Call logout endpoint (optional, for audit) - best effort, local logout continues regardless.
      await apiClient.post(`${AUTH_BASE}/logout`, {
        refreshToken: refreshToken ?? undefined,
        allSessions: false,
      });
    } catch {
      // Never log the error object: it embeds the Authorization header and refresh token.
      if (__DEV__) {
        console.error("Logout API call failed.");
      }
      // Continue with local logout even if API fails
    }

    // Clear local storage
    await SecureStorage.clearAll();
    await biometricService.disableBiometric();
  }

  async refreshToken(): Promise<AuthTokens> {
    try {
      const refreshToken = await SecureStorage.getRefreshToken();
      if (!refreshToken) {
        throw new Error("No refresh token available");
      }

      const response = await apiClient.post<AuthTokensResponse>(`${AUTH_BASE}/refresh`, {
        refreshToken,
      });

      const data = response.data;
      if (
        typeof data?.accessToken !== "string" ||
        !data.accessToken ||
        typeof data?.refreshToken !== "string" ||
        !data.refreshToken
      ) {
        throw new Error("Invalid refresh response");
      }

      const tokens = toAuthTokens(data);
      await SecureStorage.setAccessToken(tokens.accessToken);
      await SecureStorage.setRefreshToken(tokens.refreshToken);

      return tokens;
    } catch (error) {
      // Never log the error object: it embeds the Authorization header and refresh token.
      if (__DEV__) {
        console.error("Token refresh failed.");
      }
      throw error;
    }
  }

  async getCurrentUser(): Promise<User | null> {
    try {
      const response = await apiClient.get<MeResponse>(ME_PATH);
      return toUser(response.data);
    } catch {
      // Never log the error object: it embeds the Authorization header.
      if (__DEV__) {
        console.error("Failed to fetch current user.");
      }
      return null;
    }
  }

  /** Fetches `/me` and persists the tenant/user id locally, for `checkAuth`/biometric flows that read them back. */
  private async fetchAndStoreCurrentUser(): Promise<User | null> {
    const user = await this.getCurrentUser();
    if (!user) {
      return null;
    }
    await SecureStorage.setUserId(user.id);
    await SecureStorage.setTenantId(user.tenantId);
    return user;
  }

  async validateToken(): Promise<boolean> {
    try {
      const token = await SecureStorage.getAccessToken();
      if (!token) {
        return false;
      }

      // getCurrentUser swallows request errors and returns null, so a rejected token shows up as null.
      return (await this.getCurrentUser()) !== null;
    } catch {
      if (__DEV__) {
        console.error("Token validation failed.");
      }
      return false;
    }
  }

  async loginWithBiometric(
    _email: string
  ): Promise<{ user: User; tokens: AuthTokens; tenantId: string }> {
    try {
      // Authenticate with biometric
      const authenticated = await biometricService.authenticate("Login with your biometric");
      if (!authenticated) {
        throw new Error("Biometric authentication failed");
      }

      // Get stored refresh token
      const refreshToken = await SecureStorage.getRefreshToken();
      if (!refreshToken) {
        throw new Error("No stored refresh token. Please log in with password first.");
      }

      // Refresh tokens to get valid access token
      const tokens = await this.refreshToken();

      // Get user info
      const user = await this.fetchAndStoreCurrentUser();
      if (!user) {
        throw new Error("Failed to fetch user info");
      }

      return {
        user,
        tokens,
        tenantId: user.tenantId,
      };
    } catch (error) {
      if (__DEV__) {
        console.error("Biometric login failed.");
      }
      throw error;
    }
  }

  async enableBiometricLogin(): Promise<void> {
    try {
      // Check if user has successfully logged in with password at least once
      const lastEmail = await PreferenceStorage.getLastLoginEmail();
      if (!lastEmail) {
        throw new Error("Please log in with password first to enable biometric");
      }
      // Without a stored refresh token, biometric login could never succeed later.
      const refreshToken = await SecureStorage.getRefreshToken();
      if (!refreshToken) {
        throw new Error("Please log in with password first to enable biometric");
      }

      // Enable biometric (will prompt user)
      await biometricService.enableBiometric();
    } catch (error) {
      if (__DEV__) {
        console.error("Failed to enable biometric login:", error);
      }
      throw error;
    }
  }
}

export const authService = new AuthService();
