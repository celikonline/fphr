import { dateOnlyToDate, toLocalDateOnly } from "@/lib/formatters";

/** Strips the time-of-day so comparisons against `dateOnlyToDate` (always midnight) aren't skewed by "now"'s clock time. */
function atMidnight(date: Date): Date {
  return dateOnlyToDate(toLocalDateOnly(date));
}

/**
 * TK-031: client-side sanity checks mirroring the backend validators exactly, so the mobile form can
 * fail fast with a Turkish message instead of a raw server rejection - same pattern as
 * `DocumentService.ts`'s `isDocumentFileSizeAllowed`. The backend remains the source of truth; these
 * never replace server-side validation.
 */

/** `CreateAdvanceRequestValidator`/`AddExpenseItemValidator`: `Amount` must be `> 0` (`ExpenseClaimCommands.cs`). */
export function isPositiveAmount(amount: number): boolean {
  return amount > 0;
}

/** `ExpenseLimits.MaxReceiptDateDaysAhead` (`ExpenseDomainErrors.cs:53`) - a receipt may be dated at most 1 day ahead of today. */
const MAX_RECEIPT_DATE_DAYS_AHEAD = 1;

/**
 * Client-side sanity ceiling for the number of items in one expense claim - `ExpenseLimits`
 * (`ExpenseDomainErrors.cs`) has no server-side cap on item count, only per-item amount/date rules.
 */
export const EXPENSE_CLAIM_MAX_ITEMS = 20;

export function isReceiptDateAllowed(receiptDate: string, today: Date = new Date()): boolean {
  const max = atMidnight(today);
  max.setDate(max.getDate() + MAX_RECEIPT_DATE_DAYS_AHEAD);
  return dateOnlyToDate(receiptDate).getTime() <= max.getTime();
}

/** `ExpenseLimits.AdvanceDateMaxPastDays`/`AdvanceDateMaxFutureDays` (`ExpenseDomainErrors.cs:47-50`). */
const ADVANCE_DATE_MAX_PAST_DAYS = 30;
const ADVANCE_DATE_MAX_FUTURE_DAYS = 90;

export function isAdvanceRequestedDateAllowed(
  requestedDate: string,
  today: Date = new Date()
): boolean {
  const min = atMidnight(today);
  min.setDate(min.getDate() - ADVANCE_DATE_MAX_PAST_DAYS);
  const max = atMidnight(today);
  max.setDate(max.getDate() + ADVANCE_DATE_MAX_FUTURE_DAYS);
  const value = dateOnlyToDate(requestedDate).getTime();
  return value >= min.getTime() && value <= max.getTime();
}

/** `OvertimeLimits.MaxDurationMinutes` (`OvertimeRequest.cs:227`) - a single record is at most one day long. */
const OVERTIME_MAX_DURATION_MINUTES = 24 * 60;

export function isOvertimeDurationValid(durationMinutes: number): boolean {
  return (
    Number.isInteger(durationMinutes) &&
    durationMinutes >= 1 &&
    durationMinutes <= OVERTIME_MAX_DURATION_MINUTES
  );
}

/** Parses a Turkish-comma or dot-decimal amount/duration TextInput value; `null` for empty/invalid input (never throws on user-typed text). */
export function parseAmountInput(text: string): number | null {
  const normalized = text.trim().replace(",", ".");
  if (normalized.length === 0) {
    return null;
  }
  const value = Number(normalized);
  return Number.isFinite(value) ? value : null;
}
