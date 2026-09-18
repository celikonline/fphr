import { describe, expect, it } from "vitest";
import { parseDate } from "./parse-date";

const REFERENCE = new Date(2026, 8, 18); // 18 Eylül 2026 (Friday) - matches "today" for the golden set too.

describe("parseDate", () => {
  it("parses 'bugün' as the reference date", () => {
    expect(parseDate("bugün 450 TL yemek", REFERENCE)).toEqual(new Date(2026, 8, 18));
  });

  it("parses 'dün' as one day before the reference date", () => {
    expect(parseDate("Dün akşam yemeği için 450 TL masraf", REFERENCE)).toEqual(new Date(2026, 8, 17));
  });

  it("parses 'yarın'", () => {
    expect(parseDate("yarın otel ödemesi", REFERENCE)).toEqual(new Date(2026, 8, 19));
  });

  it("parses 'geçen hafta' as seven days before the reference date", () => {
    expect(parseDate("geçen hafta taksi 120 TL", REFERENCE)).toEqual(new Date(2026, 8, 11));
  });

  it("parses dd.mm.yyyy", () => {
    expect(parseDate("20.01.2026 tarihli fiş", REFERENCE)).toEqual(new Date(2026, 0, 20));
  });

  it("parses dd/mm/yyyy", () => {
    expect(parseDate("20/01/2026 tarihli fiş", REFERENCE)).toEqual(new Date(2026, 0, 20));
  });

  it("parses '20 Ocak 2026'", () => {
    expect(parseDate("20 Ocak 2026 akşam yemeği 450 TL", REFERENCE)).toEqual(new Date(2026, 0, 20));
  });

  it("parses '20 Ocak' without a year as the current year when it is in the past", () => {
    expect(parseDate("20 Ocak taksi 80 TL", REFERENCE)).toEqual(new Date(2026, 0, 20));
  });

  it("rolls an unqualified future month/day back one year (receipts are dated in the past)", () => {
    // Relative to 18 Eylül 2026, "20 Aralık" without a year would be in the future this year.
    expect(parseDate("20 Aralık otel faturası", REFERENCE)).toEqual(new Date(2025, 11, 20));
  });

  it("is case-insensitive for Turkish month names and relative words", () => {
    expect(parseDate("DÜN akşam yemeği", REFERENCE)).toEqual(new Date(2026, 8, 17));
    expect(parseDate("20 OCAK 2026", REFERENCE)).toEqual(new Date(2026, 0, 20));
  });

  it("returns null for text with no recognizable date", () => {
    expect(parseDate("akşam yemeği için 450 TL masraf", REFERENCE)).toBeNull();
  });

  it("returns null for an invalid calendar date instead of rolling it over", () => {
    expect(parseDate("31 Şubat 2026 tarihli fiş", REFERENCE)).toBeNull();
  });
});
