import {
  advanceRequestCard,
  buildRequestCards,
  expenseClaimCard,
  filterRequestCards,
  leaveRequestCard,
  overtimeRequestCard,
  punchCorrectionRequestCard,
} from "@/screens/app/requests/requestCards";
import type { LeaveRequest, OvertimeRequest } from "@/services/LeaveService";
import type { AdvanceRequest, ExpenseClaim } from "@/services/ExpenseService";
import type { PunchCorrectionRequest } from "@/services/AttendanceService";

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

function punchCorrection(overrides: Partial<PunchCorrectionRequest> = {}): PunchCorrectionRequest {
  return {
    id: "corr-1",
    date: "2026-09-18",
    requestedCheckOut: "18:00:00",
    reason: "Turnike arızalıydı",
    status: "pending",
    submittedAt: "2026-09-19T08:00:00Z",
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

    it("sorts leave and overtime by the local day they were submitted", () => {
      expect(leaveRequestCard(leave({ submittedAt: "2026-09-26T22:30:00Z" })).sortDate).toBe(
        "2026-09-27"
      );
      expect(overtimeRequestCard(overtime({ submittedAt: "2026-09-26T21:05:00Z" })).sortDate).toBe(
        "2026-09-27"
      );
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

  it("orders by submission, not by the leave's start or the overtime's work date", () => {
    const cards = buildRequestCards({
      // Starts far in the future but was submitted first.
      leave: [
        leave({
          startDate: "2026-12-01",
          endDate: "2026-12-05",
          submittedAt: "2026-09-02T09:00:00Z",
        }),
      ],
      // Worked long ago, submitted last.
      overtime: [overtime({ workDate: "2026-08-01", submittedAt: "2026-09-18T09:00:00Z" })],
      expense: [expense({ submittedAt: "2026-09-10T09:00:00Z" })],
      advance: [advance({ requestedDate: "2026-09-05" })],
      categories: [],
    });
    expect(cards.map((card) => card.kind)).toEqual(["overtime", "expense", "advance", "leave"]);
  });

  it("orders same-day submissions by submission time", () => {
    const cards = buildRequestCards({
      leave: [leave({ submittedAt: "2026-09-20T08:00:00Z" })],
      overtime: [overtime({ submittedAt: "2026-09-20T11:00:00Z" })],
      expense: [],
      advance: [],
      categories: [],
    });
    expect(cards.map((card) => card.kind)).toEqual(["overtime", "leave"]);
  });

  it("falls back to the type's own date when there is no submission timestamp", () => {
    expect(leaveRequestCard(leave({ submittedAt: "" })).sortDate).toBe("2026-09-29");
    expect(expenseClaimCard(expense({ submittedAt: null }), []).sortDate).toBe("2026-09-14");
    expect(advanceRequestCard(advance()).sortDate).toBe("2026-09-01");
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

  describe("punchCorrectionRequestCard (FG-004)", () => {
    it("maps a pending request to its requested times and warning tone; withdrawable", () => {
      const card = punchCorrectionRequestCard(
        punchCorrection({ requestedCheckIn: "08:55:00", requestedCheckOut: "18:00:00" })
      );
      expect(card).toMatchObject({
        kind: "punchCorrection",
        title: "Eksik giriş/çıkış",
        dateLabel: "18 Eyl 2026",
        detail: "08:55 – 18:00",
        statusLabel: "Bekliyor",
        tone: "warning",
        description: "Turnike arızalıydı",
        canWithdraw: true,
      });
    });

    it("shows only the requested side when only one punch is being corrected", () => {
      expect(punchCorrectionRequestCard(punchCorrection()).detail).toBe("18:00");
    });

    it("is never withdrawable once decided", () => {
      expect(punchCorrectionRequestCard(punchCorrection({ status: "approved" })).tone).toBe(
        "success"
      );
      expect(punchCorrectionRequestCard(punchCorrection({ status: "approved" })).canWithdraw).toBe(
        false
      );
      expect(punchCorrectionRequestCard(punchCorrection({ status: "rejected" })).tone).toBe(
        "danger"
      );
      expect(punchCorrectionRequestCard(punchCorrection({ status: "applyFailed" })).tone).toBe(
        "danger"
      );
      expect(punchCorrectionRequestCard(punchCorrection({ status: "withdrawn" })).tone).toBe(
        "neutral"
      );
    });

    it("sorts by the local day it was submitted, falling back to its own date", () => {
      expect(punchCorrectionRequestCard(punchCorrection({ submittedAt: "" })).sortDate).toBe(
        "2026-09-18"
      );
    });
  });

  it("includes punch-correction requests in the merged list and 'Tümü' filter", () => {
    const cards = buildRequestCards({
      leave: [],
      overtime: [],
      expense: [],
      advance: [],
      punchCorrection: [punchCorrection()],
      categories: [],
    });
    expect(cards.map((card) => card.kind)).toEqual(["punchCorrection"]);
    expect(filterRequestCards(cards, "punchCorrection")).toHaveLength(1);
  });

  it("keeps working when punchCorrection is omitted (callers built before FG-004)", () => {
    const cards = buildRequestCards({
      leave: [leave()],
      overtime: [],
      expense: [],
      advance: [],
      categories: [],
    });
    expect(cards).toHaveLength(1);
  });
});
