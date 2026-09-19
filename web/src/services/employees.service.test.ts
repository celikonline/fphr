import { describe, it, expect, vi } from "vitest";
import { toEmployeeInput, type EmployeeDetail } from "./employees.service";

vi.mock("@senseik/api-client", () => ({
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn() },
}));

const detail = {
  id: "e1",
  firstName: "Olena",
  lastName: "Kovalenko",
  nationalId: null,
  workEmail: null,
  personalEmail: null,
  workPhone: null,
  hireDate: "2024-01-01",
  birthDate: null,
  orgUnitId: null,
  managerEmployeeId: null,
  gender: "unspecified",
  maritalStatus: "unspecified",
  childCount: null,
  educationStatus: null,
  highestEducationLevel: null,
  militaryStatus: null,
  disabilityDegree: null,
  bloodType: null,
  mobilePhone: null,
  homePhone: null,
  addressLine: null,
  city: null,
  postalCode: null,
  contractType: null,
  employmentType: null,
  leaveAccrualStartDate: "2024-01-01",
} as unknown as EmployeeDetail;

describe("toEmployeeInput (senseik-058)", () => {
  it("never sends the keep-when-absent profile fields, so a section save cannot wipe them server-side", () => {
    const body = toEmployeeInput(detail);

    for (const key of ["sicilNumarasi", "nationality", "foreignIdNumber", "passportNumber", "sgkOccupationCode"]) {
      expect(body).not.toHaveProperty(key);
      expect(JSON.parse(JSON.stringify(body))).not.toHaveProperty(key);
    }
  });

  it("lets a section that owns one of them pass it (or an empty string to clear) as an override", () => {
    const body = toEmployeeInput(detail, { sicilNumarasi: "SCL-002", nationality: "foreign", passportNumber: "" });

    expect(body.sicilNumarasi).toBe("SCL-002");
    expect(body.nationality).toBe("foreign");
    expect(body.passportNumber).toBe("");
    expect(body).not.toHaveProperty("foreignIdNumber");
  });
});
