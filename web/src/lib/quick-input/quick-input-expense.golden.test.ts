/**
 * 300-scenario golden set for the Expense Quick Input parser (senseik-035), in the spirit of the backend's
 * `PayrollCalculatorGoldenSetTests.cs`: a combination generator builds every scenario and its expected result
 * programmatically (never 300 hand-typed lines), then the test asserts an aggregate accuracy threshold.
 *
 * Scoring, per the card's acceptance criteria:
 *  - A scenario "passes" when every field (date, amount+currency, category) matches its expected value -
 *    including fields that are *expected* to be unrecoverable, where "left blank" (`undefined`) is correct.
 *  - Leaving a field blank when it *was* recoverable only costs that one scenario a pass - it is a safe miss,
 *    not a wrong fill.
 *  - A "wrong fill" - any field holding a defined value that does not match its expected value - is a hard
 *    failure on its own, asserted separately with zero tolerance (never fill a field with the wrong value).
 */
import { describe, expect, it } from "vitest";
import { parseExpenseQuickInput, type ExpenseQuickInputResult } from "./parse-expense-quick-input";
import { toDateOnly } from "@/lib/dates";

const REFERENCE = new Date(2026, 8, 18); // 18 Eylül 2026 (Friday)
const MIN_ACCURACY = 0.9;

const CATEGORY_DICTIONARY: Record<string, string[]> = {
  yemek: ["yemek", "restoran", "akşam yemeği", "öğle yemeği", "kahvaltı"],
  ulasim: ["taksi", "uber", "benzin", "otobüs", "uçak bileti"],
  konaklama: ["otel", "otel faturası", "pansiyon"],
};

interface DateCase {
  label: string;
  phrase: string;
  expected: Date;
}

// 4 relative + 4 dd.mm.yyyy + 4 dd/mm/yyyy + 4 "dd Ay yyyy" + 4 "dd Ay" (no year, incl. 2 that must roll back a year) = 20.
const DATE_CASES: DateCase[] = [
  { label: "relative-today", phrase: "bugün", expected: new Date(2026, 8, 18) },
  { label: "relative-yesterday", phrase: "dün", expected: new Date(2026, 8, 17) },
  { label: "relative-tomorrow", phrase: "yarın", expected: new Date(2026, 8, 19) },
  { label: "relative-lastweek", phrase: "geçen hafta", expected: new Date(2026, 8, 11) },
  { label: "dot-1", phrase: "01.01.2026", expected: new Date(2026, 0, 1) },
  { label: "dot-2", phrase: "29.02.2024", expected: new Date(2024, 1, 29) },
  { label: "dot-3", phrase: "31.12.2025", expected: new Date(2025, 11, 31) },
  { label: "dot-4", phrase: "15.07.2026", expected: new Date(2026, 6, 15) },
  { label: "slash-1", phrase: "09/09/2026", expected: new Date(2026, 8, 9) },
  { label: "slash-2", phrase: "23/11/2025", expected: new Date(2025, 10, 23) },
  { label: "slash-3", phrase: "04/04/2027", expected: new Date(2027, 3, 4) },
  { label: "slash-4", phrase: "18/08/2026", expected: new Date(2026, 7, 18) },
  { label: "named-year-1", phrase: "12 Şubat 2026", expected: new Date(2026, 1, 12) },
  { label: "named-year-2", phrase: "28 Haziran 2026", expected: new Date(2026, 5, 28) },
  { label: "named-year-3", phrase: "3 Ekim 2025", expected: new Date(2025, 9, 3) },
  { label: "named-year-4", phrase: "17 Aralık 2024", expected: new Date(2024, 11, 17) },
  { label: "named-noyear-past-1", phrase: "5 Ocak", expected: new Date(2026, 0, 5) },
  { label: "named-noyear-past-2", phrase: "15 Mart", expected: new Date(2026, 2, 15) },
  { label: "named-noyear-rollback-1", phrase: "20 Aralık", expected: new Date(2025, 11, 20) },
  { label: "named-noyear-rollback-2", phrase: "10 Kasım", expected: new Date(2025, 10, 10) },
];

interface CategoryCase {
  label: string;
  phrase: string;
  expected: string | null;
}

// 5 yemek + 5 ulaşım + 3 konaklama + 1 out-of-dictionary word + 1 typo/near-miss of a real keyword = 15.
const CATEGORY_CASES: CategoryCase[] = [
  { label: "yemek-1", phrase: "yemek", expected: "yemek" },
  { label: "yemek-2", phrase: "restoran", expected: "yemek" },
  { label: "yemek-3", phrase: "akşam yemeği", expected: "yemek" },
  { label: "yemek-4", phrase: "öğle yemeği", expected: "yemek" },
  { label: "yemek-5", phrase: "kahvaltı", expected: "yemek" },
  { label: "ulasim-1", phrase: "taksi", expected: "ulasim" },
  { label: "ulasim-2", phrase: "uber", expected: "ulasim" },
  { label: "ulasim-3", phrase: "benzin", expected: "ulasim" },
  { label: "ulasim-4", phrase: "otobüs", expected: "ulasim" },
  { label: "ulasim-5", phrase: "uçak bileti", expected: "ulasim" },
  { label: "konaklama-1", phrase: "otel", expected: "konaklama" },
  { label: "konaklama-2", phrase: "otel faturası", expected: "konaklama" },
  { label: "konaklama-3", phrase: "pansiyon", expected: "konaklama" },
  { label: "unmatched-out-of-dictionary", phrase: "kırtasiye", expected: null },
  // Deliberate typo/near-miss of "taksi" - must NOT match ("taksii" is not a word-boundary match for "taksi"),
  // covering the card's "yazım hatası" requirement: an unrecognized spelling stays blank, never fuzzy-matched.
  { label: "unmatched-typo", phrase: "taksii", expected: null },
];

interface AmountCase {
  label: string;
  phrase: string;
  expected: { amount: number; currency: string } | null;
}

// 5 recognizable formats + 1 deliberately unrecoverable (no digits/currency) = 6.
const AMOUNT_CASES: AmountCase[] = [
  { label: "plain", phrase: "450 TL", expected: { amount: 450, currency: "TRY" } },
  { label: "comma-decimal", phrase: "450,50 TL", expected: { amount: 450.5, currency: "TRY" } },
  { label: "dot-decimal-glued", phrase: "450.50TL", expected: { amount: 450.5, currency: "TRY" } },
  { label: "grouped", phrase: "1.250 TL", expected: { amount: 1250, currency: "TRY" } },
  { label: "lira-word", phrase: "300 lira", expected: { amount: 300, currency: "TRY" } },
  { label: "unrecoverable", phrase: "yaklaşık bir masraf", expected: null },
];

interface Scenario {
  id: string;
  text: string;
  dateCase: DateCase;
  categoryCase: CategoryCase;
  amountCase: AmountCase;
}

function buildSentence(dateCase: DateCase, categoryCase: CategoryCase, amountCase: AmountCase, templateIndex: number): string {
  switch (templateIndex) {
    case 0:
      return `${dateCase.phrase} ${categoryCase.phrase} için ${amountCase.phrase} masraf yaptım.`;
    case 1:
      return `${categoryCase.phrase} masrafı: ${amountCase.phrase}, ${dateCase.phrase}.`;
    default:
      return `${amountCase.phrase} tutarında ${categoryCase.phrase} - ${dateCase.phrase}`;
  }
}

/** 20 dates x 15 categories = 300 unique (date, category) pairs; the amount case cycles through all 6 formats. */
function buildScenarios(): Scenario[] {
  const scenarios: Scenario[] = [];
  for (const dateCase of DATE_CASES) {
    for (const categoryCase of CATEGORY_CASES) {
      const index = scenarios.length;
      const amountCase = AMOUNT_CASES[index % AMOUNT_CASES.length];
      const text = buildSentence(dateCase, categoryCase, amountCase, index % 3);
      scenarios.push({ id: `${dateCase.label}|${categoryCase.label}|${amountCase.label}`, text, dateCase, categoryCase, amountCase });
    }
  }
  return scenarios;
}

const SCENARIOS = buildScenarios();

interface FieldCheck {
  matches: boolean;
  isWrongFill: boolean;
}

function checkDate(result: ExpenseQuickInputResult, scenario: Scenario): FieldCheck {
  const expected = toDateOnly(scenario.dateCase.expected);
  const matches = result.receiptDate === expected;
  return { matches, isWrongFill: result.receiptDate !== undefined && !matches };
}

function checkAmount(result: ExpenseQuickInputResult, scenario: Scenario): FieldCheck {
  const expected = scenario.amountCase.expected;
  const matches = expected === null ? result.amount === undefined : result.amount === expected.amount && result.currency === expected.currency;
  return { matches, isWrongFill: result.amount !== undefined && !matches };
}

function checkCategory(result: ExpenseQuickInputResult, scenario: Scenario): FieldCheck {
  const expected = scenario.categoryCase.expected;
  const matches = expected === null ? result.categoryCode === undefined : result.categoryCode === expected;
  return { matches, isWrongFill: result.categoryCode !== undefined && !matches };
}

describe("parseExpenseQuickInput golden set (300 scenarios)", () => {
  it("has exactly 300 generated scenarios with unique ids", () => {
    expect(SCENARIOS).toHaveLength(300);
    expect(new Set(SCENARIOS.map((scenario) => scenario.id)).size).toBe(300);
  });

  it("never fills a field with a wrong value, and correctly extracts fields in at least 90% of scenarios", () => {
    let passCount = 0;
    const wrongFills: string[] = [];
    const missedButSafe: string[] = [];

    for (const scenario of SCENARIOS) {
      const result = parseExpenseQuickInput(scenario.text, CATEGORY_DICTIONARY, REFERENCE);
      const date = checkDate(result, scenario);
      const amount = checkAmount(result, scenario);
      const category = checkCategory(result, scenario);

      if (date.isWrongFill) wrongFills.push(`${scenario.id} :: date :: got "${result.receiptDate}" for "${scenario.text}"`);
      if (amount.isWrongFill) wrongFills.push(`${scenario.id} :: amount :: got ${result.amount} ${result.currency} for "${scenario.text}"`);
      if (category.isWrongFill) wrongFills.push(`${scenario.id} :: category :: got "${result.categoryCode}" for "${scenario.text}"`);

      const pass = date.matches && amount.matches && category.matches;
      if (pass) {
        passCount += 1;
      } else {
        missedButSafe.push(scenario.id);
      }
    }

    // Hard invariant: leaving a field blank is acceptable, filling it with the wrong value never is.
    expect(wrongFills).toEqual([]);

    const accuracy = passCount / SCENARIOS.length;
    expect(passCount, `accuracy ${(accuracy * 100).toFixed(1)}% - missed (safely) ${missedButSafe.length}/${SCENARIOS.length}: ${missedButSafe.slice(0, 10).join(", ")}...`).toBeGreaterThanOrEqual(
      Math.ceil(SCENARIOS.length * MIN_ACCURACY),
    );
  });
});
