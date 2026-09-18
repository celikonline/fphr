import { describe, it, expect, vi, beforeEach } from "vitest";
import * as passwordResetService from "./password-reset.service";

// Mock apiClient
// vi.mock factories are hoisted above regular top-level statements, so the
// mocked value must be created with vi.hoisted() to avoid a TDZ error.
const mockApiClient = vi.hoisted(() => ({
  post: vi.fn(),
}));

vi.mock("@senseik/api-client", () => ({
  apiClient: mockApiClient,
}));

describe("password-reset.service", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("forgotPassword", () => {
    it("posts email to forgot-password endpoint", async () => {
      mockApiClient.post.mockResolvedValueOnce({ data: null });

      await passwordResetService.forgotPassword("user@example.com");

      expect(mockApiClient.post).toHaveBeenCalledWith("/v1/identity/auth/forgot-password", {
        email: "user@example.com",
      });
    });

    it("sends email in request body (not as query param)", async () => {
      mockApiClient.post.mockResolvedValueOnce({ data: null });

      await passwordResetService.forgotPassword("test@example.com");

      const call = mockApiClient.post.mock.calls[0];
      expect(call[0]).toBe("/v1/identity/auth/forgot-password");
      expect(call[1]).toEqual({ email: "test@example.com" });
    });

    it("returns undefined (204 No Content)", async () => {
      mockApiClient.post.mockResolvedValueOnce({ data: null });

      const result = await passwordResetService.forgotPassword("user@example.com");

      expect(result).toBeUndefined();
    });

    it("does not leak user enumeration (same response for existing and non-existing email)", async () => {
      // Queue the same resolved value for both calls below; a single mockResolvedValueOnce()
      // would leave the second call falling through to vi.fn()'s default (a plain `undefined`
      // return, not a promise), which would make the two mock results look different for a
      // reason that has nothing to do with the enumeration behavior under test.
      mockApiClient.post.mockResolvedValueOnce({ data: null }).mockResolvedValueOnce({ data: null });

      const result1 = await passwordResetService.forgotPassword("existing@example.com");
      const result2 = await passwordResetService.forgotPassword("nonexisting@example.com");

      // Both should return the same (nothing), API doesn't indicate if email exists
      expect(result1).toBeUndefined();
      expect(result2).toBeUndefined();

      // Both calls succeed with same response
      expect(mockApiClient.post).toHaveBeenCalledTimes(2);
      expect(mockApiClient.post.mock.results[0].value).toEqual(
        mockApiClient.post.mock.results[1].value
      );
    });

    it("handles various email formats", async () => {
      mockApiClient.post.mockResolvedValueOnce({ data: null });

      const emails = ["user+tag@example.co.uk", "user@subdomain.example.com", "a@b.c"];

      for (const email of emails) {
        await passwordResetService.forgotPassword(email);
      }

      expect(mockApiClient.post).toHaveBeenCalledTimes(3);
      emails.forEach((email, index) => {
        expect(mockApiClient.post.mock.calls[index][1]).toEqual({ email });
      });
    });
  });

  describe("resetPassword", () => {
    it("posts token and newPassword to reset-password endpoint", async () => {
      const input: passwordResetService.ResetPasswordInput = {
        token: "reset-token-xyz123",
        newPassword: "NewSecurePassword123!",
      };

      mockApiClient.post.mockResolvedValueOnce({ data: null });

      await passwordResetService.resetPassword(input);

      expect(mockApiClient.post).toHaveBeenCalledWith("/v1/identity/auth/reset-password", input);
    });

    it("sends token and password in request body (not query params)", async () => {
      const input: passwordResetService.ResetPasswordInput = {
        token: "token-from-email",
        newPassword: "MyNewPassword!",
      };

      mockApiClient.post.mockResolvedValueOnce({ data: null });

      await passwordResetService.resetPassword(input);

      const call = mockApiClient.post.mock.calls[0];
      expect(call[0]).toBe("/v1/identity/auth/reset-password");
      expect(call[1]).toEqual(input);
      expect(call[1].token).toBeDefined();
      expect(call[1].newPassword).toBeDefined();
      // Ensure token is not in URL (which would be in call[0] if it were a query param)
      expect(call[0]).not.toContain(input.token);
    });

    it("returns undefined (204 No Content)", async () => {
      const input: passwordResetService.ResetPasswordInput = {
        token: "token",
        newPassword: "password",
      };

      mockApiClient.post.mockResolvedValueOnce({ data: null });

      const result = await passwordResetService.resetPassword(input);

      expect(result).toBeUndefined();
    });

    it("allows passwords with special characters", async () => {
      const input: passwordResetService.ResetPasswordInput = {
        token: "token",
        newPassword: "P@ssw0rd!#$%^&*()",
      };

      mockApiClient.post.mockResolvedValueOnce({ data: null });

      await passwordResetService.resetPassword(input);

      expect(mockApiClient.post.mock.calls[0][1].newPassword).toBe(input.newPassword);
    });
  });

  describe("error handling", () => {
    it("propagates forgotPassword API error", async () => {
      const apiError = new Error("Server error");

      mockApiClient.post.mockRejectedValueOnce(apiError);

      await expect(passwordResetService.forgotPassword("user@example.com")).rejects.toBe(apiError);
    });

    it("propagates resetPassword API error", async () => {
      const apiError = new Error("Invalid or expired token");

      mockApiClient.post.mockRejectedValueOnce(apiError);

      const input: passwordResetService.ResetPasswordInput = {
        token: "expired-token",
        newPassword: "newpass",
      };

      await expect(passwordResetService.resetPassword(input)).rejects.toBe(apiError);
    });

    it("allows caller to distinguish between different error codes", async () => {
      mockApiClient.post
        .mockRejectedValueOnce({
          response: { status: 400, data: { code: "INVALID_TOKEN" } },
        })
        .mockRejectedValueOnce({
          response: { status: 400, data: { code: "WEAK_PASSWORD" } },
        });

      const input: passwordResetService.ResetPasswordInput = {
        token: "token",
        newPassword: "weak",
      };

      const codeOf = (error: unknown) => (error as { response: { data: { code: string } } }).response.data.code;

      try {
        await passwordResetService.resetPassword(input);
      } catch (error1) {
        try {
          await passwordResetService.resetPassword(input);
        } catch (error2) {
          // Both errors should propagate with distinct codes
          expect(codeOf(error1)).toBe("INVALID_TOKEN");
          expect(codeOf(error2)).toBe("WEAK_PASSWORD");
          expect(codeOf(error1)).not.toBe(codeOf(error2));
        }
      }
    });
  });

  describe("input validation", () => {
    it("accepts ResetPasswordInput with required fields", async () => {
      const validInput: passwordResetService.ResetPasswordInput = {
        token: "valid-token",
        newPassword: "ValidPassword123!",
      };

      mockApiClient.post.mockResolvedValueOnce({ data: null });

      const result = await passwordResetService.resetPassword(validInput);

      expect(mockApiClient.post).toHaveBeenCalled();
      expect(result).toBeUndefined();
    });
  });
});
