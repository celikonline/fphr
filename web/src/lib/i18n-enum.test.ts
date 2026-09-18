import { describe, it, expect } from "vitest";
import { enumLabel } from "./i18n-enum";

/** A raw i18next key always looks like `ns:some.path.here` — enumLabel must never return this shape. */
const RAW_KEY_SHAPE = /^[\w-]+:[\w.]+$/;

/** Minimal i18next-`t` stand-in: resolves from `dict`, else returns `options.defaultValue`, else the key itself. */
function fakeT(dict: Record<string, string>) {
  return (key: string, options?: Record<string, unknown>): string => {
    if (key in dict) return dict[key];
    if (options && "defaultValue" in options) return String(options.defaultValue);
    return key;
  };
}

describe("enumLabel", () => {
  it("returns the raw value's translation when the raw key matches directly", () => {
    const t = fakeT({ "recruitment:candidates.sources.manual": "Elle eklendi" });
    expect(enumLabel(t, "recruitment:candidates.sources", "manual")).toBe("Elle eklendi");
  });

  it("falls back to the lowerFirst (PascalCase -> camelCase) variant when the raw value misses", () => {
    // Backend sent PascalCase ("Manual") via .ToString(); only the camelCase key exists in locale files.
    const t = fakeT({ "recruitment:candidates.sources.manual": "Elle eklendi" });
    expect(enumLabel(t, "recruitment:candidates.sources", "Manual")).toBe("Elle eklendi");
  });

  it("humanizes the raw value when neither spelling resolves, never returning the raw i18n key", () => {
    const t = fakeT({});
    const result = enumLabel(t, "recruitment:candidates.sources", "JobSearch");
    expect(result).toBe("Job Search");
    expect(result).not.toMatch(RAW_KEY_SHAPE);
  });

  it("humanizes a snake_case raw value as a last resort", () => {
    const t = fakeT({});
    expect(enumLabel(t, "expense:status", "on_hold")).toBe("On Hold");
  });

  it("returns an empty string for null/undefined/empty raw values instead of calling t", () => {
    const t = fakeT({});
    expect(enumLabel(t, "expense:status", null)).toBe("");
    expect(enumLabel(t, "expense:status", undefined)).toBe("");
    expect(enumLabel(t, "expense:status", "")).toBe("");
  });

  it("never returns the `ns:path.X` raw-key shape for any combination of resolved/unresolved lookups", () => {
    const t = fakeT({ "recruitment:candidates.sources.manual": "Elle eklendi" });
    const rawValues = ["manual", "Manual", "careerSite", "CareerSite", "referral", "Referral", "totallyUnknown", "Unmapped_Value"];
    for (const raw of rawValues) {
      const result = enumLabel(t, "recruitment:candidates.sources", raw);
      expect(result).not.toMatch(RAW_KEY_SHAPE);
    }
  });
});
