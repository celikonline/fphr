import type { AdvanceRequestStatus, ExpenseClaimStatus } from "@/services/ExpenseService";
import i18n from "@/lib/i18n";

/**
 * TK-031: labels for the expense/advance segments of "Taleplerim" in the active language -
 * mirrors `web/public/locales/*\/expense.json` (`status.*`, `categories.*`), keys under
 * `requests:expenseStatus`, `requests:advanceStatus`, `requests:expenseCategories`.
 */
const EXPENSE_CLAIM_STATUSES: readonly ExpenseClaimStatus[] = [
  "draft",
  "submitted",
  "approved",
  "rejected",
  "paid",
];
const ADVANCE_STATUSES: readonly AdvanceRequestStatus[] = [
  "submitted",
  "approved",
  "rejected",
  "paid",
];

export function expenseClaimStatusLabel(status: ExpenseClaimStatus): string {
  return i18n.t(`requests:expenseStatus.${status}`);
}

export function advanceStatusLabel(status: AdvanceRequestStatus): string {
  return i18n.t(`requests:advanceStatus.${status}`);
}

/** Every expense claim status with its label (active language). */
export function expenseClaimStatusLabels(): Record<ExpenseClaimStatus, string> {
  return Object.fromEntries(
    EXPENSE_CLAIM_STATUSES.map((status) => [status, expenseClaimStatusLabel(status)])
  ) as Record<ExpenseClaimStatus, string>;
}

/** Every advance request status with its label (active language). */
export function advanceStatusLabels(): Record<AdvanceRequestStatus, string> {
  return Object.fromEntries(
    ADVANCE_STATUSES.map((status) => [status, advanceStatusLabel(status)])
  ) as Record<AdvanceRequestStatus, string>;
}

/** Only the two categories seeded/translated on web today; a tenant's own custom categories fall back to their raw `code`. */
const KNOWN_EXPENSE_CATEGORIES = new Set(["travel", "food"]);

export function expenseCategoryLabel(code: string): string {
  return KNOWN_EXPENSE_CATEGORIES.has(code) ? i18n.t(`requests:expenseCategories.${code}`) : code;
}
