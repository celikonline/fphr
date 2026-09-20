import { describe, expect, it } from "vitest";
import { expenseCategoryName, isSystemExpenseCategory } from "./expense-category";

const t = (key: string) => ({ "expense:categories.food": "Yiyecek ve İçecek" })[key] ?? key;

describe("expenseCategoryName", () => {
  it("translates system categories through their code, not the raw resource key", () => {
    expect(expenseCategoryName(t, { code: "food", nameKey: "expense.category.food" })).toBe("Yiyecek ve İçecek");
  });

  it("shows the literal name of tenant-defined categories", () => {
    expect(expenseCategoryName(t, { code: "gym", nameKey: "Spor Salonu" })).toBe("Spor Salonu");
  });

  it("falls back to the code when an unknown category still carries a resource-shaped key", () => {
    expect(expenseCategoryName(t, { code: "parking", nameKey: "expense.category.parking" })).toBe("parking");
  });

  it("flags only the seeded codes as system categories", () => {
    expect(isSystemExpenseCategory({ code: "travel" })).toBe(true);
    expect(isSystemExpenseCategory({ code: "gym" })).toBe(false);
  });
});
