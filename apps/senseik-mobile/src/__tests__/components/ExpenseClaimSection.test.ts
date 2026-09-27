import {
  canAddExpenseItem,
  canSubmitExpenseClaim,
  sumExpenseItemAmounts,
} from "@/components/requests/ExpenseClaimSection";
import { EXPENSE_CLAIM_MAX_ITEMS } from "@/lib/requestValidation";

describe("canAddExpenseItem (TK-031 web parity: multi-item claims)", () => {
  const validParams = {
    categoryId: "cat-1",
    amountText: "125,50",
    receiptDate: "2020-01-01",
    itemCount: 0,
  };

  it("allows adding a valid item", () => {
    expect(canAddExpenseItem(validParams)).toBe(true);
  });

  it("blocks when no category is selected", () => {
    expect(canAddExpenseItem({ ...validParams, categoryId: null })).toBe(false);
  });

  it("blocks a zero amount", () => {
    expect(canAddExpenseItem({ ...validParams, amountText: "0" })).toBe(false);
  });

  it("blocks an unparseable amount", () => {
    expect(canAddExpenseItem({ ...validParams, amountText: "" })).toBe(false);
  });

  it("blocks a receipt date more than 1 day in the future", () => {
    const farFuture = new Date();
    farFuture.setDate(farFuture.getDate() + 30);
    const [dateOnly] = farFuture.toISOString().split("T");
    expect(canAddExpenseItem({ ...validParams, receiptDate: dateOnly })).toBe(false);
  });

  it("blocks adding another item once the claim already has the max item count", () => {
    expect(canAddExpenseItem({ ...validParams, itemCount: EXPENSE_CLAIM_MAX_ITEMS })).toBe(false);
  });

  it("allows one item below the max item count", () => {
    expect(canAddExpenseItem({ ...validParams, itemCount: EXPENSE_CLAIM_MAX_ITEMS - 1 })).toBe(
      true
    );
  });
});

describe("canSubmitExpenseClaim (TK-031 web parity: at least one item required)", () => {
  it("blocks submission with no items", () => {
    expect(canSubmitExpenseClaim({ itemCount: 0 })).toBe(false);
  });

  it("allows submission with one or more items", () => {
    expect(canSubmitExpenseClaim({ itemCount: 1 })).toBe(true);
    expect(canSubmitExpenseClaim({ itemCount: 5 })).toBe(true);
  });
});

describe("sumExpenseItemAmounts (TK-031 web parity: claim total)", () => {
  it("returns 0 for an empty list", () => {
    expect(sumExpenseItemAmounts([])).toBe(0);
  });

  it("sums multiple item amounts", () => {
    expect(sumExpenseItemAmounts([{ amount: 100 }, { amount: 50.5 }, { amount: 20 }])).toBe(170.5);
  });
});
