import { canSubmitExpenseClaim } from "@/components/requests/ExpenseClaimSection";

describe("canSubmitExpenseClaim (TK-031)", () => {
  const validParams = { categoryId: "cat-1", amountText: "125,50", receiptDate: "2020-01-01" };

  it("allows a valid submission", () => {
    expect(canSubmitExpenseClaim(validParams)).toBe(true);
  });

  it("blocks when no category is selected", () => {
    expect(canSubmitExpenseClaim({ ...validParams, categoryId: null })).toBe(false);
  });

  it("blocks a zero amount", () => {
    expect(canSubmitExpenseClaim({ ...validParams, amountText: "0" })).toBe(false);
  });

  it("blocks an unparseable amount", () => {
    expect(canSubmitExpenseClaim({ ...validParams, amountText: "" })).toBe(false);
  });

  it("blocks a receipt date more than 1 day in the future", () => {
    const farFuture = new Date();
    farFuture.setDate(farFuture.getDate() + 30);
    const [dateOnly] = farFuture.toISOString().split("T");
    expect(canSubmitExpenseClaim({ ...validParams, receiptDate: dateOnly })).toBe(false);
  });
});
