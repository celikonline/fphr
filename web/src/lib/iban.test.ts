import { describe, it, expect } from "vitest";
import { validateIban, formatIban, normalizeIban, isWellFormedIban } from "./iban";

describe("iban", () => {
  describe("validateIban", () => {
    it("accepts valid Turkish IBAN", () => {
      // TR33 0006 1005 1978 6457 8413 26 (checksum: 33 passes mod-97)
      const result = validateIban("TR330006100519786457841326");
      expect(result).toBeNull();
    });

    it("accepts valid Turkish IBAN with spaces", () => {
      const result = validateIban("TR33 0006 1005 1978 6457 8413 26");
      expect(result).toBeNull();
    });

    it("accepts valid Turkish IBAN with lowercase", () => {
      const result = validateIban("tr330006100519786457841326");
      expect(result).toBeNull();
    });

    it("returns 'required' for empty string", () => {
      const result = validateIban("");
      expect(result).toBe("required");
    });

    it("returns 'required' for whitespace only", () => {
      const result = validateIban("   ");
      expect(result).toBe("required");
    });

    it("returns 'invalid' for wrong checksum", () => {
      // TR34 instead of TR33 (wrong checksum digit)
      const result = validateIban("TR340006100519786457841326");
      expect(result).toBe("invalid");
    });

    it("returns 'invalid' for wrong length TR IBAN", () => {
      // TR IBANs must be exactly 26 chars
      const result = validateIban("TR33000610051978645784");
      expect(result).toBe("invalid");
    });

    it("returns 'invalid' for non-numeric BBAN in TR", () => {
      // BBAN after TR and check digits should be all numeric
      const result = validateIban("TR33000610A51978645784132");
      expect(result).toBe("invalid");
    });

    it("returns 'invalid' for too short IBAN", () => {
      const result = validateIban("TR33 1234");
      expect(result).toBe("invalid");
    });

    it("returns 'invalid' for too long IBAN", () => {
      const result = validateIban("TR33" + "0".repeat(40));
      expect(result).toBe("invalid");
    });

    it("returns 'invalid' for missing country code", () => {
      const result = validateIban("3300061005197864578413");
      expect(result).toBe("invalid");
    });

    it("returns 'invalid' for invalid country code format", () => {
      const result = validateIban("1133000610051978645784132");
      expect(result).toBe("invalid");
    });

    it("returns 'countryUnsupported' for valid IBAN of unsupported country", () => {
      // DE is valid format but not supported (only TR is)
      const result = validateIban("DE89370400440532013000");
      expect(result).toBe("countryUnsupported");
    });

    it("handles mixed case correctly", () => {
      const result = validateIban("Tr33000610051978645784132_6");
      expect(result).toBe("invalid"); // underscore is invalid char
    });
  });

  describe("formatIban", () => {
    it("formats Turkish IBAN with groups of 4", () => {
      const result = formatIban("TR330006100519786457841326");
      expect(result).toBe("TR33 0006 1005 1978 6457 8413 26");
    });

    it("handles IBAN with existing spaces", () => {
      const result = formatIban("TR33 0006 1005 1978 6457 8413 26");
      expect(result).toBe("TR33 0006 1005 1978 6457 8413 26");
    });

    it("handles lowercase input", () => {
      const result = formatIban("tr330006100519786457841326");
      expect(result).toBe("TR33 0006 1005 1978 6457 8413 26");
    });

    it("trims trailing space", () => {
      const input = "TR330006100519786457841326";
      const result = formatIban(input);
      expect(result).not.toMatch(/\s$/);
    });
  });

  describe("normalizeIban", () => {
    it("removes spaces", () => {
      const result = normalizeIban("TR33 0006 1005 1978 6457 8413 26");
      expect(result).toBe("TR330006100519786457841326");
    });

    it("converts to uppercase", () => {
      const result = normalizeIban("tr330006100519786457841326");
      expect(result).toBe("TR330006100519786457841326");
    });

    it("handles multiple consecutive spaces", () => {
      const result = normalizeIban("TR33   0006   1005");
      expect(result).toBe("TR3300061005");
    });

    it("handles tabs and newlines", () => {
      const result = normalizeIban("TR33\t0006\n1005");
      expect(result).toBe("TR3300061005");
    });
  });

  describe("isWellFormedIban", () => {
    it("returns true for valid Turkish IBAN", () => {
      const result = isWellFormedIban("TR330006100519786457841326");
      expect(result).toBe(true);
    });

    it("returns false for invalid checksum", () => {
      const result = isWellFormedIban("TR340006100519786457841326");
      expect(result).toBe(false);
    });

    it("returns false for wrong country length", () => {
      const result = isWellFormedIban("TR33000610051978645784132"); // 25 chars, not 26
      expect(result).toBe(false);
    });

    it("returns false for non-numeric BBAN in TR", () => {
      const result = isWellFormedIban("TR33000610A519786457841326");
      expect(result).toBe(false);
    });

    it("returns true for unsupported but well-formed country", () => {
      // This is a valid (well-formed) German IBAN, even though only TR is supported by validateIban
      const germanIban = "DE89370400440532013000";
      const result = isWellFormedIban(germanIban);
      expect(result).toBe(true);
    });

    it("returns false for IBAN below minimum length", () => {
      const result = isWellFormedIban("TR33 1234");
      expect(result).toBe(false);
    });

    it("returns false for IBAN above maximum length", () => {
      const result = isWellFormedIban("TR33" + "0".repeat(40));
      expect(result).toBe(false);
    });
  });
});
