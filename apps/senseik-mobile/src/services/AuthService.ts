import { apiClient } from "@/lib/apiClient";
import { SecureStorage, PreferenceStorage } from "@/lib/storage";
import { biometricService } from "@/lib/biometric";
import { User, AuthResponse, AuthTokens } from "@/types";

export class AuthService {
  async login(email: string, password: string): Promise<AuthResponse> {
    try {
      const response = await apiClient.post<AuthResponse>("/api/auth/login", {
        email,
        password
      });

      const { user, tokens, tenantId } = response.data;

      // Store tokens and user info
      await SecureStorage.setAccessToken(tokens.accessToken);
      await SecureStorage.setRefreshToken(tokens.refreshToken);
      await SecureStorage.setTenantId(tenantId);
      await SecureStorage.setUserId(user.id);
      await PreferenceStorage.setLastLoginEmail(email);

      return response.data;
    } catch (error) {
      console.error("Login failed:", error);
      throw error;
    }
  }

  async logout(): Promise<void> {
    try {
      // Call logout endpoint (optional, for audit)
      await apiClient.post("/api/auth/logout", {});
    } catch (error) {
      console.error("Logout API call failed:", error);
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

      const response = await apiClient.post<any>("/api/auth/refresh", {
        refreshToken
      });

      const { accessToken, refreshToken: newRefreshToken } = response.data.data;

      await SecureStorage.setAccessToken(accessToken);
      await SecureStorage.setRefreshToken(newRefreshToken);

      return {
        accessToken,
        refreshToken: newRefreshToken,
        expiresIn: response.data.data.expiresIn || 900 // 15 minutes default
      };
    } catch (error) {
      console.error("Token refresh failed:", error);
      throw error;
    }
  }

  async getCurrentUser(): Promise<User | null> {
    try {
      const response = await apiClient.get<User>("/api/users/me");
      return response.data;
    } catch (error) {
      console.error("Failed to fetch current user:", error);
      return null;
    }
  }

  async validateToken(): Promise<boolean> {
    try {
      const token = await SecureStorage.getAccessToken();
      if (!token) {
        return false;
      }

      // Try to get current user to validate token
      await this.getCurrentUser();
      return true;
    } catch (error) {
      console.error("Token validation failed:", error);
      return false;
    }
  }

  async loginWithBiometric(email: string): Promise<AuthResponse> {
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
      const user = await this.getCurrentUser();
      if (!user) {
        throw new Error("Failed to fetch user info");
      }

      const tenantId = await SecureStorage.getTenantId();
      if (!tenantId) {
        throw new Error("No tenant ID stored");
      }

      return {
        user,
        tokens,
        tenantId
      };
    } catch (error) {
      console.error("Biometric login failed:", error);
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

      // Enable biometric (will prompt user)
      await biometricService.enableBiometric();
    } catch (error) {
      console.error("Failed to enable biometric login:", error);
      throw error;
    }
  }
}

export const authService = new AuthService();
