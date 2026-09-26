import {
  balanceBreakdown,
  canWithdrawLeaveRequest,
  isRequestableOnMobile,
} from "@/screens/app/LeaveScreen";
import type { LeaveBalance } from "@/services/LeaveService";

describe("isRequestableOnMobile (D2)", () => {
  it("allows a plain full-day type", () => {
    expect(
      isRequestableOnMobile({ unit: "fullDay", requiresSubstitute: false, requiresDocument: false })
    ).toBe(true);
  });

  it("allows a half-day type without substitute/document requirements", () => {
    expect(
      isRequestableOnMobile({ unit: "halfDay", requiresSubstitute: false, requiresDocument: false })
    ).toBe(true);
  });

  it("blocks an hourly type - the submit body has no startTime/endTime fields", () => {
    expect(
      isRequestableOnMobile({ unit: "hourly", requiresSubstitute: false, requiresDocument: false })
    ).toBe(false);
  });

  it("blocks a type that requires a substitute - no substituteEmployeeId picker on mobile", () => {
    expect(
      isRequestableOnMobile({ unit: "fullDay", requiresSubstitute: true, requiresDocument: false })
    ).toBe(false);
  });

  it("blocks a type that requires a document - no attachment flow on mobile", () => {
    expect(
      isRequestableOnMobile({ unit: "fullDay", requiresSubstitute: false, requiresDocument: true })
    ).toBe(false);
  });

  it("treats a missing requiresSubstitute (older API builds) as false", () => {
    expect(isRequestableOnMobile({ unit: "fullDay", requiresDocument: false })).toBe(true);
  });
});

describe("canWithdrawLeaveRequest (H1)", () => {
  it("allows withdrawing a pending request", () => {
    expect(canWithdrawLeaveRequest({ status: "pending" })).toBe(true);
  });

  it.each(["approved", "rejected", "withdrawn", "cancelled"] as const)(
    "blocks withdrawing a %s request",
    (status) => {
      expect(canWithdrawLeaveRequest({ status })).toBe(false);
    }
  );
});

/**
 * Defaults are all zero except the lifetime `totalAccrued`/`totalUsed` (kept only because the type
 * requires them - B8's second finding is precisely that these do NOT feed into `availableBalance`,
 * so `balanceBreakdown` never reads them). `availableBalance`/`currentBalance` default to a
 * self-consistent (all-zero-period) balance; tests that exercise the sum override them explicitly.
 */
function buildBalance(overrides: Partial<LeaveBalance> = {}): LeaveBalance {
  return {
    leaveTypeId: "type-1",
    currentPeriodFrom: "2026-01-01",
    currentPeriodTo: "2026-12-31",
    nextAccrualDate: "2027-01-01",
    totalAccrued: 56,
    totalUsed: 16,
    carriedOverIntoPeriod: 0,
    lastPeriodAccrued: 0,
    extraGrants: 0,
    adjustments: 0,
    currentPeriodUsed: 0,
    futureApprovedInPeriod: 0,
    currentBalance: 0,
    availableBalance: 0,
    ...overrides,
  };
}

/** `LeaveBalanceEngine.cs:223,254`: the exact server formula, used to keep test fixtures honest. */
function computeAvailableBalance(balance: LeaveBalance): number {
  return (
    balance.carriedOverIntoPeriod +
    balance.lastPeriodAccrued +
    balance.extraGrants +
    balance.adjustments -
    balance.currentPeriodUsed -
    balance.futureApprovedInPeriod
  );
}

describe("balanceBreakdown (B8)", () => {
  it("sums to availableBalance for a period with carry-over, extra grant and a negative adjustment", () => {
    const balance = buildBalance({
      carriedOverIntoPeriod: 79,
      lastPeriodAccrued: 56,
      extraGrants: 0.3333,
      adjustments: -3,
      currentPeriodUsed: 16,
      futureApprovedInPeriod: 6,
    });
    balance.availableBalance = computeAvailableBalance(balance);

    expect(balance.availableBalance).toBeCloseTo(110.3333, 6);
    expect(balanceBreakdown({ balance, pendingDays: 4 })).toBe(
      "79 devreden · 56 hak ediş · 0,3 ek hak · -3 düzeltme · 16 kullanılan · 6 planlanan (4 onay bekliyor)"
    );
  });

  it("shows a positive adjustment with an explicit + sign", () => {
    const balance = buildBalance({ adjustments: 2.5 });

    expect(balanceBreakdown({ balance })).toBe("+2,5 düzeltme");
  });

  it("omits every zero-valued item, including pendingDays", () => {
    expect(balanceBreakdown({ balance: buildBalance(), pendingDays: 0 })).toBe("");
  });

  it("shows only the pending suffix when every period item is zero but a request is pending", () => {
    expect(balanceBreakdown({ balance: buildBalance(), pendingDays: 2 })).toBe("(2 onay bekliyor)");
  });

  it("omits pendingDays when absent (older API builds)", () => {
    expect(balanceBreakdown({ balance: buildBalance({ lastPeriodAccrued: 14 }) })).toBe(
      "14 hak ediş"
    );
  });

  it("never reads the lifetime totalAccrued/totalUsed fields", () => {
    const balance = buildBalance({ totalAccrued: 999, totalUsed: 999, lastPeriodAccrued: 14 });

    expect(balanceBreakdown({ balance })).toBe("14 hak ediş");
  });
});
