import { describe, expect, it } from "vitest";
import { isValidTenantSlug, suggestTenantSlug } from "./tenant-slug";

describe("isValidTenantSlug", () => {
  it.each(["acme", "acme-corp", "a1b", "x".repeat(40)])("accepts %s", (slug) => {
    expect(isValidTenantSlug(slug)).toBe(true);
  });

  it.each([
    ["too short", "ab"],
    ["too long", "x".repeat(41)],
    ["uppercase", "Acme"],
    ["leading hyphen", "-acme"],
    ["trailing hyphen", "acme-"],
    ["double hyphen", "ac--me"],
    ["underscore", "ac_me"],
    ["space", "ac me"],
    ["turkish letter", "şirket"],
    ["empty", ""],
  ])("rejects %s", (_label, slug) => {
    expect(isValidTenantSlug(slug)).toBe(false);
  });
});

describe("suggestTenantSlug", () => {
  it("folds Turkish letters to ASCII", () => {
    expect(suggestTenantSlug("Çağrı Şirketi Öğütücü")).toBe("cagri-sirketi-ogutucu");
  });

  it("treats the dotted/dotless I correctly", () => {
    expect(suggestTenantSlug("İstanbul IŞIK")).toBe("istanbul-isik");
  });

  it("collapses punctuation and whitespace into single hyphens", () => {
    expect(suggestTenantSlug("  Acme,   Inc. & Co!  ")).toBe("acme-inc-co");
  });

  it("caps the length at 40 without leaving a trailing hyphen", () => {
    const slug = suggestTenantSlug(`${"a".repeat(39)} bcd`);
    expect(slug.length).toBeLessThanOrEqual(40);
    expect(slug.endsWith("-")).toBe(false);
  });

  it("returns an empty string when nothing usable is left", () => {
    expect(suggestTenantSlug("!!! ???")).toBe("");
  });

  it("produces slugs the validator accepts", () => {
    expect(isValidTenantSlug(suggestTenantSlug("Örnek Teknoloji A.Ş."))).toBe(true);
  });
});
