import { describe, expect, it } from "vitest";
import { parseAmount } from "./parse-amount";

describe("parseAmount", () => {
  it("parses a plain integer amount with a space before TL", () => {
    expect(parseAmount("dün akşam yemeği için 450 TL masraf")).toEqual({ amount: 450, currency: "TRY" });
  });

  it("parses a comma-decimal amount", () => {
    expect(parseAmount("taksi 450,50 TL")).toEqual({ amount: 450.5, currency: "TRY" });
  });

  it("parses a dot-decimal amount glued to TL", () => {
    expect(parseAmount("450.50TL yemek")).toEqual({ amount: 450.5, currency: "TRY" });
  });

  it("parses '... lira'", () => {
    expect(parseAmount("450 lira market alışverişi")).toEqual({ amount: 450, currency: "TRY" });
  });

  it("parses a thousands-grouped amount", () => {
    expect(parseAmount("otel faturası 1.250,75 TL")).toEqual({ amount: 1250.75, currency: "TRY" });
  });

  it("is case-insensitive for the currency marker", () => {
    expect(parseAmount("300 Lira ulaşım")).toEqual({ amount: 300, currency: "TRY" });
  });

  it("returns null when there is no currency marker", () => {
    expect(parseAmount("450 masraf yaptım")).toBeNull();
  });

  it("returns null when there is no number at all", () => {
    expect(parseAmount("birkaç yüz TL civarı taksi")).toBeNull();
  });

  it("returns null for a zero or negative-looking amount", () => {
    expect(parseAmount("0 TL yemek")).toBeNull();
  });
});
