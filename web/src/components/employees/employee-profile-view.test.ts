import { describe, expect, it } from "vitest";
import type { EmployeeDetail } from "@/services/employees.service";
import { computeTenure, profileCompletion } from "./employee-profile-view";

function employee(overrides: Partial<EmployeeDetail> = {}): EmployeeDetail {
  return {
    id: "e1",
    firstName: "Ada",
    lastName: "Naşit",
    displayName: "Ada Naşit",
    nationalId: null,
    workEmail: null,
    personalEmail: null,
    workPhone: null,
    hireDate: null,
    birthDate: null,
    status: "Active",
    orgUnitId: null,
    managerEmployeeId: null,
    managerDisplayName: null,
    userId: null,
    accountStatus: "none",
    accessType: null,
    lastInvitationSentAt: null,
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
    suspensionReason: null,
    leaveAccrualStartDate: null,
    positionStartDate: null,
    customFields: null,
    ...overrides,
  } as EmployeeDetail;
}

describe("computeTenure", () => {
  it("returns whole years, months and days between two dates", () => {
    expect(computeTenure("2013-11-14", "2026-09-16")).toEqual({ years: 12, months: 10, days: 2 });
  });

  it("borrows the length of the previous month when the day of month is earlier", () => {
    // 31 Jan -> 2 Mar (non-leap): 1 month and 2 days, not 1 month and -29 days.
    expect(computeTenure("2025-01-31", "2025-03-02")).toEqual({ years: 0, months: 1, days: 2 });
  });

  it("returns zero everywhere for the same day", () => {
    expect(computeTenure("2026-09-21", "2026-09-21")).toEqual({ years: 0, months: 0, days: 0 });
  });

  it("rolls the month deficit into the years", () => {
    expect(computeTenure("2020-12-15", "2021-02-15")).toEqual({ years: 0, months: 2, days: 0 });
  });

  it("is null when the end date is before the start date", () => {
    expect(computeTenure("2026-09-22", "2026-09-21")).toBeNull();
  });
});

describe("profileCompletion", () => {
  it("is zero for an empty file; 'unspecified' enums count as not filled in", () => {
    expect(profileCompletion(employee())).toEqual({ completed: 0, total: 20, percent: 0 });
  });

  it("counts every filled tracked field", () => {
    const result = profileCompletion(
      employee({
        nationalId: "12345678901",
        birthDate: "1983-02-08",
        gender: "female",
        workEmail: "a@b.co",
        city: "İstanbul",
      })
    );

    expect(result.completed).toBe(5);
    expect(result.percent).toBe(25);
  });

  it("treats empty strings like missing values", () => {
    expect(profileCompletion(employee({ city: "", addressLine: "" })).completed).toBe(0);
  });
});
