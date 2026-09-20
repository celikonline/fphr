import { describe, expect, it } from "vitest";
import { ISO_COUNTRY_CODES, countryOptions, isIsoCountryCode } from "./country-codes";

describe("country codes", () => {
  it("lists the 249 assigned ISO 3166-1 alpha-2 codes once each", () => {
    expect(ISO_COUNTRY_CODES).toHaveLength(249);
    expect(new Set(ISO_COUNTRY_CODES).size).toBe(249);
    expect(ISO_COUNTRY_CODES.every((code) => /^[A-Z]{2}$/.test(code))).toBe(true);
  });

  it("accepts assigned codes and rejects unassigned or malformed ones", () => {
    expect(isIsoCountryCode("TR")).toBe(true);
    expect(isIsoCountryCode("de")).toBe(true);
    expect(isIsoCountryCode("ZZ")).toBe(false);
    expect(isIsoCountryCode("XX")).toBe(false);
    expect(isIsoCountryCode("T")).toBe(false);
    expect(isIsoCountryCode("")).toBe(false);
  });

  it("localizes names with Intl.DisplayNames", () => {
    const tr = countryOptions("tr");
    expect(tr.find((o) => o.value === "DE")?.label).toBe("Almanya");
    const en = countryOptions("en");
    expect(en.find((o) => o.value === "DE")?.label).toBe("Germany");
  });

  it("returns one option per code, sorted by localized label", () => {
    const options = countryOptions("tr");
    expect(options).toHaveLength(249);
    const labels = options.map((o) => o.label);
    expect(labels).toEqual([...labels].sort((a, b) => a.localeCompare(b, "tr")));
  });
});
