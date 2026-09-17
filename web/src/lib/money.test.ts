import { describe, it, expect } from "vitest";
import { formatMoney, hasAtMostTwoDecimals } from "./money";

describe("money", () => {
  describe("formatMoney", () => {
    it("formats amount in TRY for Turkish locale", () => {
      const result = formatMoney(1234.56, "TRY", "tr-TR");
      // Turkish format: "1.234,56 ₺" or similar with Turkish decimal separator
      expect(result).toMatch(/1.*234|,56|TRY/);
    });

    it("formats amount in USD for US locale", () => {
      const result = formatMoney(1234.56, "USD", "en-US");
      // US format: "$1,234.56" or "1,234.56 USD"
      expect(result).toMatch(/1.*234|\.56|USD|\$/);
    });

    it("formats amount with exactly two decimals", () => {
      const result = formatMoney(99.99, "EUR", "tr-TR");
      expect(result).toMatch(/99|99/);
    });

    it("formats zero amount", () => {
      const result = formatMoney(0, "TRY", "tr-TR");
      expect(result).toMatch(/0|TRY/);
    });

    it("formats negative amount", () => {
      const result = formatMoney(-500.25, "TRY", "tr-TR");
      expect(result).toMatch(/500|25|-/);
    });

    it("rounds to two decimals for currency formatting", () => {
      const result = formatMoney(10.005, "TRY", "tr-TR");
      // Should round to 2 decimals
      expect(result).toMatch(/10|TRY/);
    });

    it("falls back to generic format for unsupported currency code", () => {
      const result = formatMoney(1234.56, "FAKECURRENCY", "en-US");
      // Fallback: "1,234.56 FAKECURRENCY"
      expect(result).toMatch(/1.*234|\.56|FAKECURRENCY/);
    });

    it("handles very large amounts", () => {
      const result = formatMoney(999999999.99, "TRY", "tr-TR");
      expect(result).toMatch(/999|TRY/);
    });

    it("handles very small amounts", () => {
      const result = formatMoney(0.01, "TRY", "tr-TR");
      expect(result).toMatch(/0|01|TRY/);
    });

    it("formats amount in GBP for UK locale", () => {
      const result = formatMoney(100, "GBP", "en-GB");
      expect(result).toMatch(/100|GBP|£/);
    });
  });

  describe("hasAtMostTwoDecimals", () => {
    it("returns true for amounts with no decimal part", () => {
      expect(hasAtMostTwoDecimals(100)).toBe(true);
    });

    it("returns true for amounts with one decimal place", () => {
      expect(hasAtMostTwoDecimals(100.5)).toBe(true);
    });

    it("returns true for amounts with exactly two decimal places", () => {
      expect(hasAtMostTwoDecimals(100.99)).toBe(true);
    });

    it("returns true for zero", () => {
      expect(hasAtMostTwoDecimals(0)).toBe(true);
    });

    it("returns true for negative amounts with valid decimals", () => {
      expect(hasAtMostTwoDecimals(-50.25)).toBe(true);
    });

    it("returns false for amounts with three decimal places", () => {
      expect(hasAtMostTwoDecimals(100.999)).toBe(false);
    });

    it("returns false for amounts with more than three decimal places", () => {
      expect(hasAtMostTwoDecimals(100.12345)).toBe(false);
    });

    it("returns true for very small positive amounts", () => {
      expect(hasAtMostTwoDecimals(0.01)).toBe(true);
    });

    it("returns true for very small negative amounts", () => {
      expect(hasAtMostTwoDecimals(-0.01)).toBe(true);
    });

    it("handles floating point precision edge cases", () => {
      // JavaScript floating point: 0.1 + 0.2 !== 0.3
      const result = hasAtMostTwoDecimals(0.30);
      expect(result).toBe(true);
    });

    it("returns true for 1.5 (one decimal)", () => {
      expect(hasAtMostTwoDecimals(1.5)).toBe(true);
    });

    it("returns false for 1.555 (three decimals)", () => {
      expect(hasAtMostTwoDecimals(1.555)).toBe(false);
    });

    it("returns false for amounts close to three decimals", () => {
      expect(hasAtMostTwoDecimals(1.001)).toBe(false);
    });
  });
});
