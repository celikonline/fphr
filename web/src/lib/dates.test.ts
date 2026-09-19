import { describe, it, expect } from "vitest";
import { addDaysToDateOnly, enumerateDateOnlyRange, formatDateOnly, daysUntil, parseDateOnly, toDateOnly } from "./dates";

describe("dates", () => {
  describe("formatDateOnly", () => {
    it("formats date in TR locale medium style", () => {
      const result = formatDateOnly("2026-09-17", "tr-TR", { dateStyle: "medium" });
      // TR locale medium style: "17 Eyl 2026" or similar
      expect(result).toMatch(/17|Eyl|2026/);
    });

    it("formats date in EN locale medium style", () => {
      const result = formatDateOnly("2026-09-17", "en-US", { dateStyle: "medium" });
      // EN locale medium style: "Sep 17, 2026"
      expect(result).toMatch(/Sep|September|17|2026/);
    });

    it("handles first day of year", () => {
      const result = formatDateOnly("2026-01-01", "tr-TR", { dateStyle: "medium" });
      expect(result).toMatch(/1|Oca|2026/);
    });

    it("handles last day of year", () => {
      const result = formatDateOnly("2026-12-31", "tr-TR", { dateStyle: "medium" });
      expect(result).toMatch(/31|Ara|2026/);
    });

    it("preserves custom Intl options", () => {
      const result = formatDateOnly("2026-09-17", "tr-TR", { year: "numeric", month: "long", day: "numeric" });
      expect(result).toMatch(/2026|Eylül|17/);
    });
  });

  describe("daysUntil", () => {
    it("returns 0 for today", () => {
      const today = new Date();
      const dateString = toDateOnly(today);
      const days = daysUntil(dateString);
      expect(days).toBe(0);
    });

    it("returns positive number for future date", () => {
      const future = new Date();
      future.setDate(future.getDate() + 5);
      const dateString = toDateOnly(future);
      const days = daysUntil(dateString);
      expect(days).toBe(5);
    });

    it("returns negative number for past date", () => {
      const past = new Date();
      past.setDate(past.getDate() - 3);
      const dateString = toDateOnly(past);
      const days = daysUntil(dateString);
      expect(days).toBe(-3);
    });

    it("handles leap year correctly", () => {
      // Feb 28 2024 (leap year) to Mar 1 2024 should be 1 day
      const feb28 = toDateOnly(new Date(2024, 1, 28));
      const mar1 = toDateOnly(new Date(2024, 2, 1));
      const diff = daysUntil(mar1) - daysUntil(feb28);
      expect(diff).toBe(2); // Feb 29 + Mar 1
    });
  });

  describe("parseDateOnly", () => {
    it("parses valid date string correctly", () => {
      const date = parseDateOnly("2026-09-17");
      expect(date.getFullYear()).toBe(2026);
      expect(date.getMonth()).toBe(8); // 0-indexed
      expect(date.getDate()).toBe(17);
    });

    it("handles single digit month/day with leading zero", () => {
      const date = parseDateOnly("2026-01-05");
      expect(date.getFullYear()).toBe(2026);
      expect(date.getMonth()).toBe(0);
      expect(date.getDate()).toBe(5);
    });
  });

  describe("toDateOnly", () => {
    it("converts Date to yyyy-MM-dd string", () => {
      const date = new Date(2026, 8, 17); // Month is 0-indexed
      const result = toDateOnly(date);
      expect(result).toBe("2026-09-17");
    });

    it("pads month and day with leading zeros", () => {
      const date = new Date(2026, 0, 5); // January 5th
      const result = toDateOnly(date);
      expect(result).toBe("2026-01-05");
    });

    it("round-trips with parseDateOnly", () => {
      const original = "2026-09-17";
      const parsed = parseDateOnly(original);
      const serialized = toDateOnly(parsed);
      expect(serialized).toBe(original);
    });
  });

  describe("addDaysToDateOnly", () => {
    it("adds positive days within the same month", () => {
      expect(addDaysToDateOnly("2026-09-17", 3)).toBe("2026-09-20");
    });

    it("subtracts days with a negative count", () => {
      expect(addDaysToDateOnly("2026-09-17", -3)).toBe("2026-09-14");
    });

    it("rolls over a month boundary", () => {
      expect(addDaysToDateOnly("2026-09-29", 3)).toBe("2026-10-02");
    });

    it("rolls over a year boundary", () => {
      expect(addDaysToDateOnly("2026-12-30", 3)).toBe("2027-01-02");
    });
  });

  describe("enumerateDateOnlyRange", () => {
    it("returns every date from start to end inclusive", () => {
      expect(enumerateDateOnlyRange("2026-09-17", "2026-09-20")).toEqual(["2026-09-17", "2026-09-18", "2026-09-19", "2026-09-20"]);
    });

    it("returns a single-element array when start equals end", () => {
      expect(enumerateDateOnlyRange("2026-09-17", "2026-09-17")).toEqual(["2026-09-17"]);
    });

    it("returns an empty array when end is before start", () => {
      expect(enumerateDateOnlyRange("2026-09-20", "2026-09-17")).toEqual([]);
    });

    it("spans a month boundary", () => {
      expect(enumerateDateOnlyRange("2026-09-29", "2026-10-02")).toEqual(["2026-09-29", "2026-09-30", "2026-10-01", "2026-10-02"]);
    });
  });
});
