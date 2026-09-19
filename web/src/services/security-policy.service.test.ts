import { describe, it, expect, vi, beforeEach } from "vitest";
import * as securityPolicyService from "./security-policy.service";
import type { SecurityPolicy } from "@/types";

// Mock apiClient - mirrors access-settings.service.test.ts (same GET/PUT round-trip shape).
const mockApiClient = vi.hoisted(() => ({
  get: vi.fn(),
  put: vi.fn(),
}));

vi.mock("@senseik/api-client", () => ({
  apiClient: mockApiClient,
}));

describe("security-policy.service", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("getSecurityPolicy", () => {
    it("fetches the security policy from the correct endpoint", async () => {
      const mockPolicy: SecurityPolicy = { requireMfaForRoles: ["Owner", "Admin"] };
      mockApiClient.get.mockResolvedValueOnce({ data: mockPolicy });

      const result = await securityPolicyService.getSecurityPolicy();

      expect(mockApiClient.get).toHaveBeenCalledWith("/v1/identity/settings/security");
      expect(result).toEqual(mockPolicy);
    });

    it("propagates a load error", async () => {
      const apiError = new Error("Server error");
      mockApiClient.get.mockRejectedValueOnce(apiError);

      await expect(securityPolicyService.getSecurityPolicy()).rejects.toBe(apiError);
    });
  });

  describe("updateSecurityPolicy", () => {
    it("sends the updated role list to the correct endpoint", async () => {
      const updated: SecurityPolicy = { requireMfaForRoles: ["PayrollSpecialist"] };
      mockApiClient.put.mockResolvedValueOnce({ data: null });

      await securityPolicyService.updateSecurityPolicy(updated);

      expect(mockApiClient.put).toHaveBeenCalledWith("/v1/identity/settings/security", updated);
    });

    it("propagates a save error (e.g. missing identity.security.manage)", async () => {
      const apiError = new Error("Permission denied");
      mockApiClient.put.mockRejectedValueOnce(apiError);

      await expect(securityPolicyService.updateSecurityPolicy({ requireMfaForRoles: [] })).rejects.toBe(apiError);
    });
  });
});
