/**
 * Locale-aware day-count formatting: leave balances/day totals come back from the API as raw
 * decimals (e.g. accrual rounding can produce "41.333333333333336"). The mobile UI is Turkish-only
 * (see `LeaveService.ts`), so round to at most one decimal and use the Turkish decimal comma; whole
 * numbers show no decimal at all ("41,3" gün, "5" gün, not "5,0" gün).
 */
export function formatDays(value: number): string {
  const rounded = Math.round(value * 10) / 10;
  return rounded.toLocaleString("tr-TR", { minimumFractionDigits: 0, maximumFractionDigits: 1 });
}

/**
 * Formats a `Date` as the `DateOnly` ("yyyy-MM-dd") string the API expects, using the device's
 * local calendar date - never UTC. `Date#toISOString()` converts to UTC first, so in UTC+3 any
 * local time before 03:00 lands on the *previous* UTC day, silently shifting query ranges (and
 * month boundaries) by a day.
 */
export function toLocalDateOnly(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}
