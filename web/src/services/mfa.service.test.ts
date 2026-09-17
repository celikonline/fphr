import { describe, it, expect, vi, beforeEach } from "vitest";
import * as mfaService from "./mfa.service";

// Mock apiClient
const mockApiClient = {
  get: vi.fn(),
  post: vi.fn(),
};

vi.mock("@senseik/api-client", () => ({
  apiClient: mockApiClient,
}));

vi.mock("@/services/me.service", () => ({
  ME_PATH: "/v1/identity/me",
}));

describe("mfa.service", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("getMfaStatus", () => {
    it("fetches MFA status from correct endpoint", async () => {
      const mockStatus: mfaService.MfaStatus = {
        enabled: true,
        pendingEnrollment: false,
      };

      mockApiClient.get.mockResolvedValueOnce({ data: mockStatus });

      const result = await mfaService.getMfaStatus();

      expect(mockApiClient.get).toHaveBeenCalledWith("/v1/identity/me/mfa");
      expect(result).toEqual(mockStatus);
    });

    it("correctly interprets enabled flag", async () => {
      const mockStatus: mfaService.MfaStatus = {
        enabled: false,
        pendingEnrollment: true,
      };

      mockApiClient.get.mockResolvedValueOnce({ data: mockStatus });

      const result = await mfaService.getMfaStatus();

      expect(result.enabled).toBe(false);
      expect(result.pendingEnrollment).toBe(true);
    });
  });

  describe("enrollMfa", () => {
    it("posts to correct enrollment endpoint", async () => {
      const mockEnrollment: mfaService.MfaEnrollment = {
        secret: "JBSWY3DPEBLW64TMMQ======",
        otpAuthUri: "otpauth://totp/app:user@example.com?secret=JBSWY3DPEBLW64TMMQ%3D%3D%3D%3D%3D%3D&issuer=app",
      };

      mockApiClient.post.mockResolvedValueOnce({ data: mockEnrollment });

      const result = await mfaService.enrollMfa();

      expect(mockApiClient.post).toHaveBeenCalledWith("/v1/identity/me/mfa/enroll");
      expect(result).toEqual(mockEnrollment);
    });

    it("returns secret and otpAuthUri with correct camelCase naming", async () => {
      const mockEnrollment: mfaService.MfaEnrollment = {
        secret: "TESTABC123",
        otpAuthUri: "otpauth://totp/example",
      };

      mockApiClient.post.mockResolvedValueOnce({ data: mockEnrollment });

      const result = await mfaService.enrollMfa();

      expect(result).toHaveProperty("secret");
      expect(result).toHaveProperty("otpAuthUri");
      expect(typeof result.secret).toBe("string");
      expect(typeof result.otpAuthUri).toBe("string");
    });
  });

  describe("verifyMfaEnrollment", () => {
    it("posts verification code to correct endpoint", async () => {
      const code = "123456";

      mockApiClient.post.mockResolvedValueOnce({ data: null });

      await mfaService.verifyMfaEnrollment(code);

      expect(mockApiClient.post).toHaveBeenCalledWith("/v1/identity/me/mfa/verify", { code });
    });

    it("sends code in request body", async () => {
      mockApiClient.post.mockResolvedValueOnce({ data: null });

      await mfaService.verifyMfaEnrollment("654321");

      const call = mockApiClient.post.mock.calls[0];
      expect(call[1]).toEqual({ code: "654321" });
    });

    it("returns void on successful verification", async () => {
      mockApiClient.post.mockResolvedValueOnce({ data: null });

      const result = await mfaService.verifyMfaEnrollment("123456");

      expect(result).toBeUndefined();
    });
  });

  describe("disableMfa", () => {
    it("posts disable request with code to correct endpoint", async () => {
      mockApiClient.post.mockResolvedValueOnce({ data: null });

      await mfaService.disableMfa("789012");

      expect(mockApiClient.post).toHaveBeenCalledWith("/v1/identity/me/mfa/disable", { code: "789012" });
    });

    it("returns void on successful disable", async () => {
      mockApiClient.post.mockResolvedValueOnce({ data: null });

      const result = await mfaService.disableMfa("999999");

      expect(result).toBeUndefined();
    });
  });

  describe("error handling", () => {
    it("propagates enrollment API error", async () => {
      const apiError = new Error("Server error");

      mockApiClient.post.mockRejectedValueOnce(apiError);

      await expect(mfaService.enrollMfa()).rejects.toBe(apiError);
    });

    it("propagates verification API error", async () => {
      const apiError = new Error("Invalid code");

      mockApiClient.post.mockRejectedValueOnce(apiError);

      await expect(mfaService.verifyMfaEnrollment("000000")).rejects.toBe(apiError);
    });

    it("propagates disable API error", async () => {
      const apiError = new Error("Code verification failed");

      mockApiClient.post.mockRejectedValueOnce(apiError);

      await expect(mfaService.disableMfa("111111")).rejects.toBe(apiError);
    });
  });
});
