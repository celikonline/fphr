import i18n, { currentLanguage, type AppLanguage } from "@/lib/i18n";

/**
 * FG-001: the only place app languages map to Intl locale tags - every date/number/currency
 * format and locale-aware casing goes through `activeLocale()` instead of a hard-coded tag.
 */
const LOCALE_TAGS: Record<AppLanguage, string> = {
  tr: "tr-TR",
  en: "en-GB",
};

/** Intl locale tag of the active app language ("tr-TR" / "en-GB"). */
export function activeLocale(): string {
  return LOCALE_TAGS[currentLanguage()];
}

/** Locale-aware upper-casing (Turkish dotted/dotless i) - use instead of `textTransform`. */
export function toLocaleUpper(value: string, locale: string = activeLocale()): string {
  return value.toLocaleUpperCase(locale);
}

/** Locale-aware lower-casing, see `toLocaleUpper`. */
export function toLocaleLower(value: string, locale: string = activeLocale()): string {
  return value.toLocaleLowerCase(locale);
}

/** `Date#toLocaleDateString` in the active locale. */
export function formatDate(
  date: Date,
  options: Intl.DateTimeFormatOptions,
  locale: string = activeLocale()
): string {
  return date.toLocaleDateString(locale, options);
}

/** `Date#toLocaleString` (date and time) in the active locale. */
export function formatDateTime(
  date: Date,
  options: Intl.DateTimeFormatOptions,
  locale: string = activeLocale()
): string {
  return date.toLocaleString(locale, options);
}

/**
 * Locale-aware day-count formatting: leave balances/day totals come back from the API as raw
 * decimals (e.g. accrual rounding can produce "41.333333333333336"). Rounds to at most one decimal
 * in the active locale ("41,3" in Turkish, "41.3" in English); whole numbers show no decimal at
 * all ("5", not "5,0").
 */
export function formatDays(value: number, locale: string = activeLocale()): string {
  const rounded = Math.round(value * 10) / 10;
  return rounded.toLocaleString(locale, { minimumFractionDigits: 0, maximumFractionDigits: 1 });
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
export function formatDateWithWeekday(value: string, locale: string = activeLocale()): string {
  const date = dateOnlyToDate(value);
  const datePart = date.toLocaleDateString(locale, {
    day: "2-digit",
    month: "short",
    year: "numeric",
  });
  const weekdayPart = date.toLocaleDateString(locale, { weekday: "short" });
  return `${datePart} ${weekdayPart}`;
}

/** "2 sa 30 dk" / "2 h 30 min" - shared by the timesheet screens and the hourly-leave duration preview. */
export function formatDurationMinutes(totalMinutes: number): string {
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  return i18n.t("common:duration.hoursMinutes", { hours, minutes });
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
export function formatRelativeTime(
  iso: string,
  now: Date = new Date(),
  locale: string = activeLocale()
): string {
  const then = new Date(iso);
  if (Number.isNaN(then.getTime())) {
    return iso;
  }

  const diffMs = now.getTime() - then.getTime();
  if (diffMs < MINUTE_MS) {
    return i18n.t("common:relativeTime.justNow");
  }
  if (diffMs < HOUR_MS) {
    return i18n.t("common:relativeTime.minutesAgo", { count: Math.floor(diffMs / MINUTE_MS) });
  }
  if (diffMs < DAY_MS) {
    return i18n.t("common:relativeTime.hoursAgo", { count: Math.floor(diffMs / HOUR_MS) });
  }
  if (diffMs < RELATIVE_TIME_MAX_DAYS * DAY_MS) {
    return i18n.t("common:relativeTime.daysAgo", { count: Math.floor(diffMs / DAY_MS) });
  }
  return then.toLocaleDateString(locale, { day: "2-digit", month: "short", year: "numeric" });
}

/** TK-031: "1.234,56 ₺" - TRY formatting for expense/advance/overtime amounts (TRY is the only payroll-reimbursed currency), in the active locale. */
export function formatCurrencyTRY(amount: number, locale: string = activeLocale()): string {
  return new Intl.NumberFormat(locale, { style: "currency", currency: "TRY" }).format(amount);
}
