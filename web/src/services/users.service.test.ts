import { describe, expect, it } from "vitest";
import {
  deriveAccessType,
  isHiddenRole,
  visibleRoles,
  withKeptHiddenRoleIds,
  type RoleRef,
} from "./users.service";

const role = (code: string): RoleRef => ({ id: `r-${code}`, code, name: code });

describe("deriveAccessType", () => {
  it("returns null when none of the user-group roles is assigned", () => {
    expect(deriveAccessType([])).toBeNull();
    expect(deriveAccessType([role("custom_ops")])).toBeNull();
  });

  it("maps each user-group role code 1:1", () => {
    expect(deriveAccessType([role("HR")])).toBe("HR");
    expect(deriveAccessType([role("PayrollSpecialist")])).toBe("PayrollSpecialist");
    expect(deriveAccessType([role("Employee")])).toBe("Employee");
  });

  it("shows an owner (who always also has HR) as HR, whatever the role order", () => {
    expect(deriveAccessType([role("Owner"), role("HR")])).toBe("HR");
  });

  it("applies HR > PayrollSpecialist > Employee precedence regardless of role order", () => {
    expect(deriveAccessType([role("Employee"), role("PayrollSpecialist")])).toBe(
      "PayrollSpecialist"
    );
    expect(deriveAccessType([role("Employee"), role("PayrollSpecialist"), role("HR")])).toBe("HR");
  });

  it("ignores the removed legacy access types", () => {
    expect(deriveAccessType([role("Owner")])).toBeNull();
    expect(deriveAccessType([role("Admin")])).toBeNull();
  });
});

describe("hidden system roles", () => {
  it("hides Owner and SuperAdmin only", () => {
    expect(isHiddenRole("Owner")).toBe(true);
    expect(isHiddenRole("SuperAdmin")).toBe(true);
    expect(isHiddenRole("HR")).toBe(false);
    expect(isHiddenRole("Employee")).toBe(false);
  });

  it("filters hidden roles out of a role list, keeping order", () => {
    const roles = [role("Owner"), role("HR"), role("custom_ops"), role("SuperAdmin")];
    expect(visibleRoles(roles).map((r) => r.code)).toEqual(["HR", "custom_ops"]);
  });

  it("re-appends hidden roles the user already has when saving a picker selection", () => {
    const current = [role("Owner"), role("HR")];
    expect(withKeptHiddenRoleIds(current, ["r-HR", "r-custom_ops"])).toEqual([
      "r-HR",
      "r-custom_ops",
      "r-Owner",
    ]);
  });

  it("does not add hidden roles the user did not have, nor duplicate ids", () => {
    expect(withKeptHiddenRoleIds([role("Employee")], ["r-HR"])).toEqual(["r-HR"]);
    expect(withKeptHiddenRoleIds([role("Owner")], ["r-Owner"])).toEqual(["r-Owner"]);
  });
});
