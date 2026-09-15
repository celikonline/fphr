import { authService } from "@/services/AuthService";
import * as SecureStore from "expo-secure-store";
import { apiClient } from "@/lib/apiClient";

jest.mock("@/lib/apiClient");
jest.mock("@/lib/storage");
jest.mock("@/lib/biometric");

describe("AuthService", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe("login", () => {
    it("should login successfully with email and password", async () => {
      const mockResponse = {
        data: {
          user: {
            id: "user-123",
            email: "test@example.com",
            firstName: "John",
            lastName: "Doe",
            tenantId: "tenant-123",
            role: "Employee",
            permissions: [],
            isActive: true,
            createdAt: "2024-01-01",
            updatedAt: "2024-01-01"
          },
          tokens: {
            accessToken: "access-token",
            refreshToken: "refresh-token",
            expiresIn: 900
          },
          tenantId: "tenant-123"
        }
      };

      (apiClient.post as jest.Mock).mockResolvedValue(mockResponse);

      const result = await authService.login("test@example.com", "password123");

      expect(result).toEqual(mockResponse.data);
      expect(apiClient.post).toHaveBeenCalledWith("/api/auth/login", {
        email: "test@example.com",
        password: "password123"
      });
    });

    it("should throw error on login failure", async () => {
      const mockError = new Error("Invalid credentials");
      (apiClient.post as jest.Mock).mockRejectedValue(mockError);

      await expect(authService.login("test@example.com", "wrong")).rejects.toThrow();
    });
  });

  describe("logout", () => {
    it("should logout successfully", async () => {
      (apiClient.post as jest.Mock).mockResolvedValue({ data: {} });

      await authService.logout();

      expect(apiClient.post).toHaveBeenCalledWith("/api/auth/logout", {});
    });
  });

  describe("validateToken", () => {
    it("should validate token successfully", async () => {
      (SecureStore.getItemAsync as jest.Mock).mockResolvedValue("valid-token");
      (apiClient.get as jest.Mock).mockResolvedValue({
        data: { id: "user-123", email: "test@example.com" }
      });

      const result = await authService.validateToken();

      expect(result).toBe(true);
    });

    it("should return false if no token exists", async () => {
      (SecureStore.getItemAsync as jest.Mock).mockResolvedValue(null);

      const result = await authService.validateToken();

      expect(result).toBe(false);
    });
  });
});
