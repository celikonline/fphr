import {
  ADVANCE_STATUS_LABELS,
  EXPENSE_CLAIM_STATUS_LABELS,
  expenseCategoryLabel,
} from "@/lib/expenseLabels";

describe("EXPENSE_CLAIM_STATUS_LABELS (TK-031)", () => {
  it("has a Turkish label for every ExpenseClaimStatus", () => {
    expect(EXPENSE_CLAIM_STATUS_LABELS).toEqual({
      draft: "Taslak",
      submitted: "Onay bekliyor",
      approved: "Onaylandı",
      rejected: "Reddedildi",
      paid: "Ödendi",
    });
  });
});

describe("ADVANCE_STATUS_LABELS (TK-031)", () => {
  it("has a Turkish label for every AdvanceRequestStatus", () => {
    expect(ADVANCE_STATUS_LABELS).toEqual({
      submitted: "Onay bekliyor",
      approved: "Onaylandı",
      rejected: "Reddedildi",
      paid: "Ödendi",
    });
  });
});

describe("expenseCategoryLabel (TK-031)", () => {
  it("translates the seeded travel category", () => {
    expect(expenseCategoryLabel("travel")).toBe("Yolculuk ve Ulaşım");
  });

  it("translates the seeded food category", () => {
    expect(expenseCategoryLabel("food")).toBe("Yiyecek ve İçecek");
  });

  it("falls back to the raw code for a tenant-specific custom category", () => {
    expect(expenseCategoryLabel("client_gifts")).toBe("client_gifts");
  });
});
