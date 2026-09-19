/**
 * Quick Input (senseik-035): rule-based, fully local free-text → form-field parsers. No network calls, no
 * dictionaries beyond what's passed in or defined here (KVKK: nothing about the employee leaves the browser).
 * Form-independent primitives live in `parse-date`/`parse-amount`/`parse-keyword`; `parse-expense-quick-input`
 * is the first form-specific schema built on top of them (pilot: the Expense claim item form).
 */
export { parseDate, findDateMatch, type DateMatch } from "./parse-date";
export { parseAmount, findAmountMatch, type ParsedAmount, type AmountMatch } from "./parse-amount";
export { parseKeyword, findKeywordMatch, type KeywordMatch } from "./parse-keyword";
export {
  parseExpenseQuickInput,
  DEFAULT_EXPENSE_CATEGORY_KEYWORDS,
  type ExpenseQuickInputResult,
} from "./parse-expense-quick-input";
