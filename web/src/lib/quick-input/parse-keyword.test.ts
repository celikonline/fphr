import { describe, expect, it } from "vitest";
import { parseKeyword } from "./parse-keyword";

const DICTIONARY: Record<string, string[]> = {
  yemek: ["yemek", "restoran", "akşam yemeği", "öğle yemeği"],
  ulasim: ["taksi", "uber", "benzin", "otobüs"],
  konaklama: ["otel", "konaklama"],
};

describe("parseKeyword", () => {
  it("matches a single keyword", () => {
    expect(parseKeyword("taksi ile ofise gittim", DICTIONARY)).toBe("ulasim");
  });

  it("prefers the longer, more specific phrase over a shorter one that also matches", () => {
    // "yemek" alone would match the "yemek" category, but "akşam yemeği" is the more specific phrase present.
    expect(parseKeyword("akşam yemeği için 450 TL", DICTIONARY)).toBe("yemek");
  });

  it("is case-insensitive, including Turkish dotted/dotless I", () => {
    expect(parseKeyword("TAKSİ ile gittim", DICTIONARY)).toBe("ulasim");
    expect(parseKeyword("OTEL faturası", DICTIONARY)).toBe("konaklama");
  });

  it("does not match a keyword that only appears as a substring of another word", () => {
    // "taksi" must not match inside "taksitli" (installment), a real confusable in Turkish expense text.
    expect(parseKeyword("taksitli avans talebi", DICTIONARY)).toBeNull();
  });

  it("matches a keyword with a Turkish letter at the phrase boundary", () => {
    expect(parseKeyword("öğle yemeği molası", DICTIONARY)).toBe("yemek");
  });

  it("returns null when nothing in the dictionary matches", () => {
    expect(parseKeyword("kırtasiye malzemesi aldım", DICTIONARY)).toBeNull();
  });
});
