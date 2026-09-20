import { describe, expect, it } from "vitest";
import { isEligibleForGender } from "./leave-shared";

// Denetim raporu A6: the "İzin Ekle" list hides gender-restricted types from the other gender; unknown gender never restricts.
describe("isEligibleForGender", () => {
  it("offers unrestricted types to everyone", () => {
    expect(isEligibleForGender("any", "female")).toBe(true);
    expect(isEligibleForGender("any", "male")).toBe(true);
    expect(isEligibleForGender(undefined, "male")).toBe(true);
  });

  it("restricts a male-only type (Babalık) to men", () => {
    expect(isEligibleForGender("male", "male")).toBe(true);
    expect(isEligibleForGender("male", "female")).toBe(false);
  });

  it("restricts a female-only type (Doğum/Süt) to women", () => {
    expect(isEligibleForGender("female", "female")).toBe(true);
    expect(isEligibleForGender("female", "male")).toBe(false);
  });

  it.each(["unspecified", "other", null, undefined] as const)("does not restrict an unknown or unlisted gender (%s)", (gender) => {
    expect(isEligibleForGender("male", gender)).toBe(true);
    expect(isEligibleForGender("female", gender)).toBe(true);
  });
});
