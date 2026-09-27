import type { AdvanceRequestStatus, ExpenseClaimStatus } from "@/services/ExpenseService";

/**
 * TK-031: Turkish labels for the expense/advance segments of "Taleplerim" - mirrors
 * `web/public/locales/tr/expense.json` (`status.*`, `categories.*`). The mobile app has no i18n
 * layer (Turkish-only, same as `LeaveService.ts`/`leaveTypeNames.ts`).
 */
export const EXPENSE_CLAIM_STATUS_LABELS: Record<ExpenseClaimStatus, string> = {
  draft: "Taslak",
  submitted: "Onay bekliyor",
  approved: "Onaylandı",
  rejected: "Reddedildi",
  paid: "Ödendi",
};

export const ADVANCE_STATUS_LABELS: Record<AdvanceRequestStatus, string> = {
  submitted: "Onay bekliyor",
  approved: "Onaylandı",
  rejected: "Reddedildi",
  paid: "Ödendi",
};

/** Only the two categories seeded/translated on web today; a tenant's own custom categories fall back to their raw `code`. */
const EXPENSE_CATEGORY_LABELS: Record<string, string> = {
  travel: "Yolculuk ve Ulaşım",
  food: "Yiyecek ve İçecek",
};

export function expenseCategoryLabel(code: string): string {
  return EXPENSE_CATEGORY_LABELS[code] ?? code;
}
