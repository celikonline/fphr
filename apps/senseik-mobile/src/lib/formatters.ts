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

/** Parses a `DateOnly` ("yyyy-MM-dd") string into a local `Date` at midnight - the inverse of `toLocalDateOnly`. */
export function dateOnlyToDate(value: string): Date {
  const date = new Date(`${value}T00:00:00`);
  return Number.isNaN(date.getTime()) ? new Date() : date;
}

/**
 * "16 Kas 2026 Pzt": day, short month, year, short weekday - the format the date picker fields
 * (`DateField`) show once a date is picked, instead of a raw "yyyy-aa-gg" text box.
 */
export function formatDateWithWeekday(value: string): string {
  const date = dateOnlyToDate(value);
  const datePart = date.toLocaleDateString("tr-TR", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  });
  const weekdayPart = date.toLocaleDateString("tr-TR", { weekday: "short" });
  return `${datePart} ${weekdayPart}`;
}

/** "2 sa 30 dk" - shared by the timesheet screens and the hourly-leave duration preview. */
export function formatDurationMinutes(totalMinutes: number): string {
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  return `${hours} sa ${minutes} dk`;
}

/** Parses a `TimeOnly`-shaped "HH:mm" string; `null` when malformed (never throws on user-typed input). */
export function parseTimeOnly(value: string): { hours: number; minutes: number } | null {
  const match = /^(\d{2}):(\d{2})$/.exec(value);
  if (!match) return null;
  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  if (hours > 23 || minutes > 59) return null;
  return { hours, minutes };
}

/** Builds a `Date` carrying `value` ("HH:mm") as its wall-clock time (today's date, ignored by callers) - what the native time picker's `value` prop expects. Falls back to 09:00 when malformed. */
export function timeOnlyToDate(value: string): Date {
  const parsed = parseTimeOnly(value);
  const date = new Date();
  date.setHours(parsed?.hours ?? 9, parsed?.minutes ?? 0, 0, 0);
  return date;
}

/** Inverse of `timeOnlyToDate`: reads a `Date`'s wall-clock time as "HH:mm". */
export function dateToTimeOnly(date: Date): string {
  return `${String(date.getHours()).padStart(2, "0")}:${String(date.getMinutes()).padStart(2, "0")}`;
}

const MINUTE_MS = 60_000;
const HOUR_MS = 60 * MINUTE_MS;
const DAY_MS = 24 * HOUR_MS;
/** Beyond a week, a relative count stops being useful - show the actual date instead (N1). */
const RELATIVE_TIME_MAX_DAYS = 7;

/**
 * N1: "5 dk önce" / "2 sa önce" / "3 gün önce" style relative time for the notifications list.
 * Falls back to an absolute date once the gap exceeds a week. `now` is a parameter (not `new
 * Date()` internally) so this stays pure and testable without mocking the clock.
 */
export function formatRelativeTime(iso: string, now: Date = new Date()): string {
  const then = new Date(iso);
  if (Number.isNaN(then.getTime())) {
    return iso;
  }

  const diffMs = now.getTime() - then.getTime();
  if (diffMs < MINUTE_MS) {
    return "az önce";
  }
  if (diffMs < HOUR_MS) {
    return `${Math.floor(diffMs / MINUTE_MS)} dk önce`;
  }
  if (diffMs < DAY_MS) {
    return `${Math.floor(diffMs / HOUR_MS)} sa önce`;
  }
  if (diffMs < RELATIVE_TIME_MAX_DAYS * DAY_MS) {
    return `${Math.floor(diffMs / DAY_MS)} gün önce`;
  }
  return then.toLocaleDateString("tr-TR", { day: "2-digit", month: "short", year: "numeric" });
}
