import { describe, expect, it } from "vitest";
import { roleDisplayName } from "./role-name";

const dict: Record<string, string> = { "users:roleNames.HRSpecialist": "İK Uzmanı" };
const t = (key: string, options?: Record<string, unknown>) => dict[key] ?? String(options?.defaultValue ?? key);

describe("roleDisplayName", () => {
  it("translates system role codes", () => {
    expect(roleDisplayName(t, { id: "1", code: "HRSpecialist", name: "HRSpecialist" })).toBe("İK Uzmanı");
  });

  it("keeps a custom role's own name", () => {
    expect(roleDisplayName(t, { id: "2", code: "fleet_lead", name: "Filo Sorumlusu" })).toBe("Filo Sorumlusu");
  });

  it("falls back to the code when a custom role has no name", () => {
    expect(roleDisplayName(t, { id: "3", code: "fleet_lead", name: null })).toBe("fleet_lead");
  });
});
