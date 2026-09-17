import { describe, it, expect } from "vitest";
import { decodeJwtPayload, getImpersonatorId } from "./jwt";

describe("jwt", () => {
  describe("decodeJwtPayload", () => {
    it("returns null for null input", () => {
      expect(decodeJwtPayload(null)).toBeNull();
    });

    it("returns null for undefined input", () => {
      expect(decodeJwtPayload(undefined)).toBeNull();
    });

    it("returns null for empty string", () => {
      expect(decodeJwtPayload("")).toBeNull();
    });

    it("returns null for token with no dots", () => {
      expect(decodeJwtPayload("nodots")).toBeNull();
    });

    it("returns null for token with only one dot", () => {
      expect(decodeJwtPayload("header.payload")).toBeNull();
    });

    it("returns null for token with empty payload part", () => {
      const malformedToken = "header..signature";
      expect(decodeJwtPayload(malformedToken)).toBeNull();
    });

    it("returns null for invalid base64 payload", () => {
      // Invalid base64 characters
      const token = "header.!!!.signature";
      expect(decodeJwtPayload(token)).toBeNull();
    });

    it("does not throw on invalid base64", () => {
      const token = "header.@@@.signature";
      expect(() => decodeJwtPayload(token)).not.toThrow();
    });

    it("decodes valid JWT payload", () => {
      // Create a valid base64url encoded payload: {"sub":"123","name":"John"}
      const payload = JSON.stringify({ sub: "123", name: "John" });
      const base64url = btoa(payload)
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");

      const token = `header.${base64url}.signature`;
      const result = decodeJwtPayload(token);

      expect(result).toEqual({ sub: "123", name: "John" });
    });

    it("handles base64url padding correctly", () => {
      // Test with payload that needs padding
      const payload = JSON.stringify({ id: "user123" });
      const base64url = btoa(payload)
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");

      const token = `header.${base64url}.signature`;
      const result = decodeJwtPayload(token);

      expect(result).toEqual({ id: "user123" });
    });

    it("handles base64url - and _ characters correctly", () => {
      // These characters are replaced in base64url encoding
      const payload = JSON.stringify({ data: "test-with_special_chars" });
      const base64url = btoa(payload)
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");

      const token = `header.${base64url}.signature`;
      const result = decodeJwtPayload(token);

      expect(result).toEqual({ data: "test-with_special_chars" });
    });

    it("preserves Turkish characters in payload", () => {
      const payload = JSON.stringify({ name: "Şükrü Öztürk", email: "sukru@example.com" });
      const base64url = btoa(payload)
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");

      const token = `header.${base64url}.signature`;
      const result = decodeJwtPayload(token);

      expect(result?.name).toBe("Şükrü Öztürk");
      expect(result?.email).toBe("sukru@example.com");
    });

    it("preserves Unicode characters in payload", () => {
      const payload = JSON.stringify({
        name: "李明",
        title: "مهندس",
        emoji: "🚀",
      });
      const base64url = btoa(payload)
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");

      const token = `header.${base64url}.signature`;
      const result = decodeJwtPayload(token);

      expect(result?.name).toBe("李明");
      expect(result?.title).toBe("مهندس");
      expect(result?.emoji).toBe("🚀");
    });

    it("returns null for invalid JSON in payload", () => {
      // base64url encoded invalid JSON
      const invalidJson = btoa("{ invalid json")
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");

      const token = `header.${invalidJson}.signature`;
      expect(decodeJwtPayload(token)).toBeNull();
    });

    it("handles payload with various data types", () => {
      const payload = JSON.stringify({
        sub: "user-123",
        iat: 1234567890,
        exp: 1234571490,
        roles: ["admin", "user"],
        permissions: { read: true, write: false },
        isActive: true,
      });

      const base64url = btoa(payload)
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");

      const token = `header.${base64url}.signature`;
      const result = decodeJwtPayload(token);

      expect(result?.sub).toBe("user-123");
      expect(result?.iat).toBe(1234567890);
      expect(Array.isArray(result?.roles)).toBe(true);
      expect((result?.roles as string[])).toContain("admin");
      expect(result?.isActive).toBe(true);
    });
  });

  describe("getImpersonatorId", () => {
    it("returns null for null token", () => {
      expect(getImpersonatorId(null)).toBeNull();
    });

    it("returns null for undefined token", () => {
      expect(getImpersonatorId(undefined)).toBeNull();
    });

    it("returns null for token without act claim", () => {
      const payload = JSON.stringify({ sub: "user-123" });
      const base64url = btoa(payload)
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");

      const token = `header.${base64url}.signature`;
      expect(getImpersonatorId(token)).toBeNull();
    });

    it("returns null for empty act claim", () => {
      const payload = JSON.stringify({ act: "" });
      const base64url = btoa(payload)
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");

      const token = `header.${base64url}.signature`;
      expect(getImpersonatorId(token)).toBeNull();
    });

    it("returns null for non-string act claim", () => {
      const payload = JSON.stringify({ act: 123 });
      const base64url = btoa(payload)
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");

      const token = `header.${base64url}.signature`;
      expect(getImpersonatorId(token)).toBeNull();
    });

    it("returns act claim value when present and non-empty string", () => {
      const impersonatorId = "support-user-456";
      const payload = JSON.stringify({ sub: "user-123", act: impersonatorId });
      const base64url = btoa(payload)
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");

      const token = `header.${base64url}.signature`;
      expect(getImpersonatorId(token)).toBe(impersonatorId);
    });

    it("returns act claim for impersonation token (support access grant)", () => {
      const supportUserId = "support-admin-789";
      const payload = JSON.stringify({
        sub: "customer-user",
        act: supportUserId,
        iss: "SenseIK",
        iat: Date.now(),
      });

      const base64url = btoa(payload)
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");

      const token = `header.${base64url}.signature`;
      expect(getImpersonatorId(token)).toBe(supportUserId);
    });

    it("returns null for regular token (no impersonation)", () => {
      const payload = JSON.stringify({
        sub: "regular-user",
        name: "John Doe",
        roles: ["user"],
      });

      const base64url = btoa(payload)
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");

      const token = `header.${base64url}.signature`;
      expect(getImpersonatorId(token)).toBeNull();
    });

    it("returns null if token cannot be decoded", () => {
      const malformedToken = "invalid.token.structure";
      expect(getImpersonatorId(malformedToken)).toBeNull();
    });
  });
});
