import {
  advanceRequestCard,
  buildRequestCards,
  expenseClaimCard,
  filterRequestCards,
  leaveRequestCard,
  overtimeRequestCard,
} from "@/screens/app/requests/requestCards";
import type { LeaveRequest, OvertimeRequest } from "@/services/LeaveService";
import type { AdvanceRequest, ExpenseClaim } from "@/services/ExpenseService";

function leave(overrides: Partial<LeaveRequest> = {}): LeaveRequest {
  return {
    id: "leave-1",
    employeeId: "e-1",
    leaveTypeId: "t-1",
    leaveTypeCode: "annual",
    startDate: "2026-09-29",
    endDate: "2026-10-03",
    calculatedDays: 4,
    status: "pending",
    submittedAt: "2026-09-20T10:00:00Z",
    leaveTypeName: "Yıllık izin",
    ...overrides,
  };
}

function overtime(overrides: Partial<OvertimeRequest> = {}): OvertimeRequest {
  return {
    id: "ot-1",
    employeeId: "e-1",
    workDate: "2026-09-12",
    startTime: "18:00:00",
    durationMinutes: 150,
    startAt: "",
    endAt: "",
    dayKind: "workday",
    overlapWithScheduleMinutes: 0,
    payableMinutes: 150,
    status: "approved",
    submittedAt: "2026-09-12T20:00:00Z",
    payRateKind: "rate50",
    payRateMultiplier: 1.5,
    ...overrides,
  };
}

function expense(overrides: Partial<ExpenseClaim> = {}): ExpenseClaim {
  return {
    id: "ex-1",
    employeeId: "e-1",
    totalAmount: 125.5,
    status: "submitted",
    submittedAt: "2026-09-15T08:00:00Z",
    items: [
      {
        id: "item-1",
        categoryId: "cat-food",
        amount: 125.5,
        receiptDate: "2026-09-14",
        taxAmount: 0,
        currency: "TRY",
      },
    ],
    ...overrides,
  };
}

function advance(overrides: Partial<AdvanceRequest> = {}): AdvanceRequest {
  return {
    id: "adv-1",
    employeeId: "e-1",
    amount: 1000,
    currency: "TRY",
    requestedDate: "2026-09-01",
    isInstallmentRequested: false,
    status: "rejected",
    ...overrides,
  };
}

describe("request cards (Taleplerim)", () => {
  it("maps a leave request to its type name, short range, day count and pending tone", () => {
    const card = leaveRequestCard(leave());
    expect(card).toMatchObject({
      kind: "leave",
      title: "Yıllık izin",
      dateLabel: "29 Eyl → 3 Eki",
      detail: "4 gün",
      statusLabel: "Onay bekliyor",
      tone: "warning",
      canWithdraw: true,
    });
  });

  it("falls back to the leave type code and does not offer withdraw once decided", () => {
    const card = leaveRequestCard(leave({ leaveTypeName: null, status: "approved" }));
    expect(card.title).toBe("annual");
    expect(card.tone).toBe("success");
    expect(card.canWithdraw).toBe(false);
  });

  it("maps an overtime request to date, start time and duration", () => {
    const card = overtimeRequestCard(overtime());
    expect(card).toMatchObject({
      kind: "overtime",
      title: "Fazla mesai",
      dateLabel: "12 Eyl 2026 · 18:00",
      detail: "2 sa 30 dk",
      statusLabel: "Onaylandı",
      tone: "success",
      canWithdraw: false,
    });
    expect(overtimeRequestCard(overtime({ status: "pending" })).canWithdraw).toBe(true);
  });

  it("maps an expense claim to its category, receipt date and amount; never withdrawable", () => {
    const card = expenseClaimCard(expense(), [{ id: "cat-food", code: "food" }]);
    expect(card).toMatchObject({
      kind: "expense",
      title: "Yiyecek ve İçecek",
      dateLabel: "14 Eyl 2026",
      statusLabel: "Onay bekliyor",
      tone: "warning",
      canWithdraw: false,
    });
    expect(card.detail).toContain("125,50");
  });

  it("titles an expense claim 'Masraf' when its category is unknown", () => {
    expect(expenseClaimCard(expense(), []).title).toBe("Masraf");
  });

  describe("submittedAt day in a positive UTC offset", () => {
    const originalTZ = process.env.TZ;

    beforeAll(() => {
      // Deterministic regardless of the host: the UTC-day bug only shows east of UTC (Turkey, UTC+3).
      process.env.TZ = "Europe/Istanbul";
    });

    afterAll(() => {
      process.env.TZ = originalTZ;
    });

    it("dates and sorts an itemless claim by the local day it was submitted, not the UTC day", () => {
      // 22:30 UTC on the 26th is 01:30 on the 27th in Istanbul.
      const card = expenseClaimCard(
        expense({ items: [], submittedAt: "2026-09-26T22:30:00Z" }),
        []
      );
      expect(card.dateLabel).toBe("27 Eyl 2026");
      expect(card.sortDate).toBe("2026-09-27");
    });
  });

  it("lists every item of a multi-item expense claim as an extra line", () => {
    const claim = expense({
      items: [
        {
          id: "a",
          categoryId: "cat-food",
          amount: 10,
          receiptDate: "2026-09-14",
          taxAmount: 0,
          currency: "TRY",
        },
        {
          id: "b",
          categoryId: "cat-x",
          amount: 20,
          receiptDate: "2026-09-13",
          taxAmount: 0,
          currency: "TRY",
        },
      ],
    });
    const card = expenseClaimCard(claim, [{ id: "cat-food", code: "food" }]);
    expect(card.lines).toHaveLength(2);
    expect(card.lines[0]).toContain("Yiyecek ve İçecek · 14 Eyl 2026");
    expect(card.lines[1]).toContain("cat-x · 13 Eyl 2026");
  });

  it("shows a single-item claim's item description when the claim itself has none (TK-031)", () => {
    const [item] = expense().items;
    const card = expenseClaimCard(expense({ items: [{ ...item, description: "Taksi" }] }), []);
    expect(card.description).toBe("Taksi");
    expect(expenseClaimCard(expense(), []).description).toBeNull();
  });

  it("prefers the claim-level description over the item's", () => {
    const [item] = expense().items;
    const claim = expense({ description: "Ziyaret", items: [{ ...item, description: "Taksi" }] });
    expect(expenseClaimCard(claim, []).description).toBe("Ziyaret");
  });

  it("appends each item's description to its line on a multi-item claim (TK-031)", () => {
    const [item] = expense().items;
    const claim = expense({
      items: [
        { ...item, id: "a", description: "Taksi" },
        { ...item, id: "b", description: null },
      ],
    });
    const card = expenseClaimCard(claim, []);
    expect(card.description).toBeNull();
    expect(card.lines[0]).toMatch(/ · Taksi$/);
    expect(card.lines[1]).not.toContain("Taksi");
  });

  it("maps an advance request to its requested date, amount and danger tone when rejected", () => {
    const card = advanceRequestCard(advance());
    expect(card).toMatchObject({
      kind: "advance",
      title: "Avans",
      dateLabel: "1 Eyl 2026",
      statusLabel: "Reddedildi",
      tone: "danger",
    });
    expect(card.detail).toContain("1.000");
  });

  it("merges every kind newest first", () => {
    const cards = buildRequestCards({
      leave: [leave()],
      overtime: [overtime()],
      expense: [expense()],
      advance: [advance()],
      categories: [],
    });
    expect(cards.map((card) => card.kind)).toEqual(["leave", "expense", "overtime", "advance"]);
  });

  it("filters by kind, 'all' keeping everything", () => {
    const cards = buildRequestCards({
      leave: [leave()],
      overtime: [overtime()],
      expense: [],
      advance: [advance()],
      categories: [],
    });
    expect(filterRequestCards(cards, "all")).toHaveLength(3);
    expect(filterRequestCards(cards, "overtime").map((card) => card.id)).toEqual(["ot-1"]);
    expect(filterRequestCards(cards, "expense")).toEqual([]);
  });
});
