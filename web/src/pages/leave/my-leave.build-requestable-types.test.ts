import { describe, expect, it } from "vitest";
import { buildRequestableTypes } from "./my-leave";
import type { EmployeeLeaveBalances, RequestableLeaveType } from "@/services/leave.service";

// senseik-039: "+ Talep" dropdown'ı, /leave/me/leave-types çağrısının döndürdüğü ham anahtarları (ör.
// "leave.type.annual") artık backend değil frontend'in `useLeaveName()` hook'u çözüyor (bkz. leave-shared.tsx).
// Bu test gerçek i18n kurulumu olmadan, `leaveName`'in her iki yolda da (API'den gelen tür listesi VE bakiye-only
// fallback) çağrıldığını doğruluyor — regresyon: birinin unutulması "+ Talep" listesinin yeniden İngilizce/ham
// anahtar göstermesine yol açar.
function stubLeaveName(name: string): string {
  return name.startsWith("leave.") ? `[çevrildi] ${name}` : name;
}

function requestableType(overrides: Partial<RequestableLeaveType> = {}): RequestableLeaveType {
  return {
    id: "type-1",
    code: "annual",
    name: "leave.type.annual",
    description: null,
    unit: "fullDay",
    isPaid: true,
    hasBalance: true,
    perRequestMaxDays: null,
    requiresDescription: false,
    requiresDocument: false,
    countsNonWorkingDays: false,
    ...overrides,
  };
}

function emptyBalances(): EmployeeLeaveBalances {
  return {
    employeeId: "employee-1",
    hireDate: "2020-01-01",
    leaveAccrualStartDate: "2020-01-01",
    ruleSetId: "rule-set-1",
    balances: [],
  };
}

describe("buildRequestableTypes (senseik-039)", () => {
  it("resolves the raw resource key through leaveName when the /leave-types call succeeds", () => {
    const types = [requestableType()];
    const [option] = buildRequestableTypes(emptyBalances(), types, stubLeaveName);
    expect(option.name).toBe("[çevrildi] leave.type.annual");
  });

  it("passes a free-form (non-key) name through unchanged", () => {
    const types = [requestableType({ name: "Özel izin türü" })];
    const [option] = buildRequestableTypes(emptyBalances(), types, stubLeaveName);
    expect(option.name).toBe("Özel izin türü");
  });

  it("resolves balance-only fallback names through leaveName when the /leave-types call fails", () => {
    const balances: EmployeeLeaveBalances = {
      ...emptyBalances(),
      balances: [
        {
          leaveTypeName: "leave.type.sick",
          leaveTypeCode: "sick",
          balance: {
            leaveTypeId: "type-2",
            availableBalance: 5,
            currentBalance: 5,
            totalAccrued: 5,
            totalUsed: 0,
            adjustments: 0,
            futureApprovedInPeriod: 0,
            currentPeriodFrom: "2026-01-01",
            currentPeriodTo: "2026-12-31",
            currentPeriodUsed: 0,
            lastPeriodAccrued: 0,
            nextPeriodApproved: 0,
            carriedOverIntoPeriod: 0,
            extraGrants: 0,
            nextAccrualDate: "2027-01-01",
          },
        },
      ],
    };

    const [option] = buildRequestableTypes(balances, undefined, stubLeaveName);
    expect(option.name).toBe("[çevrildi] leave.type.sick");
  });
});

describe("buildRequestableTypes (denetim A5)", () => {
  it("carries the substitute/document flags of the API type into the form option", () => {
    const [option] = buildRequestableTypes(emptyBalances(), [requestableType({ requiresSubstitute: true, requiresDocument: true })], stubLeaveName);
    expect(option.requiresSubstitute).toBe(true);
    expect(option.requiresDocument).toBe(true);
  });

  it("treats a missing requiresSubstitute (older API build) as false", () => {
    const [option] = buildRequestableTypes(emptyBalances(), [requestableType()], stubLeaveName);
    expect(option.requiresSubstitute).toBe(false);
  });
});
