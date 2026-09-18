/**
 * Quick Input schema for the Expense claim item form (senseik-035 pilot). Combines the generic
 * `parseDate` / `parseAmount` / `parseKeyword` primitives into the four fields the "add receipt" mini-form
 * needs. Never fills a field it isn't sure about — an unrecognized field is left `undefined` for the user
 * to complete by hand, rather than risking a wrong value.
 */
import { findAmountMatch } from "./parse-amount";
import { findDateMatch } from "./parse-date";
import { findKeywordMatch } from "./parse-keyword";
import { toDateOnly } from "@/lib/dates";

export interface ExpenseQuickInputResult {
  /** Matched category dictionary key (e.g. a category `code`); caller resolves it to a concrete `categoryId`. */
  categoryCode?: string;
  amount?: number;
  currency?: string;
  /** Backend `DateOnly` string ("yyyy-MM-dd"), same contract as `<DatePicker>`. */
  receiptDate?: string;
  /** Leftover free text once the matched date/amount/category phrases are stripped out; may be empty. */
  description: string;
}

/**
 * Default category keyword dictionary for the pilot form's two seeded categories (`travel`, `food` in
 * `KNOWN_CATEGORY_CODES`). Callers should merge in the tenant's live category codes/names when available
 * (done in the payments page, which also knows each category's translated label).
 */
export const DEFAULT_EXPENSE_CATEGORY_KEYWORDS: Record<string, string[]> = {
  food: ["yemek", "restoran", "akşam yemeği", "öğle yemeği", "kahvaltı", "kahve", "market"],
  travel: ["taksi", "uber", "benzin", "otobüs", "uçak bileti", "otopark", "yakıt", "bilet"],
};

const WHITESPACE_RE = /\s+/g;
const EDGE_PUNCTUATION_RE = /^[\s,.;:!?'"()-]+|[\s,.;:!?'"()-]+$/g;

/** Removes the given (non-overlapping) spans from `text` and collapses the leftover whitespace/punctuation. */
function stripSpans(text: string, spans: Array<{ start: number; end: number }>): string {
  const sorted = [...spans].sort((a, b) => a.start - b.start);
  let result = "";
  let cursor = 0;
  for (const span of sorted) {
    result += text.slice(cursor, span.start);
    cursor = span.end;
  }
  result += text.slice(cursor);
  return result.replace(WHITESPACE_RE, " ").replace(EDGE_PUNCTUATION_RE, "").trim();
}

/**
 * Fills as much of the Expense item form (category, amount, date, description) as it can confidently read
 * from `text`. `categoryKeywords` defaults to the two seeded category codes; pass the tenant's live catalog
 * (see `buildExpenseCategoryKeywords`) to also match custom category names.
 */
export function parseExpenseQuickInput(
  text: string,
  categoryKeywords: Record<string, string[]> = DEFAULT_EXPENSE_CATEGORY_KEYWORDS,
  referenceDate: Date = new Date(),
): ExpenseQuickInputResult {
  const dateMatch = findDateMatch(text, referenceDate);
  const amountMatch = findAmountMatch(text);
  const keywordMatch = findKeywordMatch(text, categoryKeywords);

  const spans = [dateMatch, amountMatch, keywordMatch].filter((match): match is NonNullable<typeof match> => match !== null);
  const description = stripSpans(text, spans);

  return {
    categoryCode: keywordMatch?.key,
    amount: amountMatch?.amount,
    currency: amountMatch?.currency,
    receiptDate: dateMatch ? toDateOnly(dateMatch.date) : undefined,
    description,
  };
}
