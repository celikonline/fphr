import { describe, expect, it } from "vitest";
import { parseExpenseQuickInput } from "./parse-expense-quick-input";

const REFERENCE = new Date(2026, 8, 18); // 18 Eylül 2026

describe("parseExpenseQuickInput", () => {
  it("fills category, amount, date and a leftover description from the card's own example", () => {
    const result = parseExpenseQuickInput("Dün akşam yemeği için 450 TL masraf", undefined, REFERENCE);

    expect(result.categoryCode).toBe("food");
    expect(result.amount).toBe(450);
    expect(result.currency).toBe("TRY");
    expect(result.receiptDate).toBe("2026-09-17");
    expect(result.description).not.toContain("450");
    expect(result.description).not.toContain("TL");
  });

  it("leaves unrecognized fields undefined instead of guessing", () => {
    const result = parseExpenseQuickInput("kırtasiye malzemesi aldım", undefined, REFERENCE);

    expect(result.categoryCode).toBeUndefined();
    expect(result.amount).toBeUndefined();
    expect(result.currency).toBeUndefined();
    expect(result.receiptDate).toBeUndefined();
    expect(result.description.length).toBeGreaterThan(0);
  });

  it("never fills the wrong category when the amount/date crowd out the keyword span", () => {
    const result = parseExpenseQuickInput("taksi 120,50 TL 05.03.2026", undefined, REFERENCE);

    expect(result.categoryCode).toBe("travel");
    expect(result.amount).toBe(120.5);
    expect(result.receiptDate).toBe("2026-03-05");
  });

  it("accepts a custom category dictionary", () => {
    const result = parseExpenseQuickInput("otel faturası 900 TL", { accommodation: ["otel", "konaklama"] }, REFERENCE);

    expect(result.categoryCode).toBe("accommodation");
    expect(result.amount).toBe(900);
  });
});
