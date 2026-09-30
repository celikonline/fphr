/**
 * Pure date helpers for the JS "Tarih seç" sheet (`DateRangeSheet`). Everything works on local
 * `DateOnly` strings ("yyyy-MM-dd") built from year/month/day numbers - never through
 * `Date#toISOString()`, which converts to UTC and shifts the day in UTC+3 before 03:00.
 */
import i18n from "@/lib/i18n";

export type DateRangeMode = "single" | "range";

function calendarNames(key: "months" | "monthsShort" | "weekdaysShort"): string[] {
  return i18n.t(`common:calendar.${key}`, { returnObjects: true }) as string[];
}

/** "Eylül" / "September" (active language), `month0` 0-11. */
export function monthName(month0: number): string {
  return calendarNames("months")[month0];
}

/** "Eyl" / "Sep" (active language), `month0` 0-11. */
export function monthNameShort(month0: number): string {
  return calendarNames("monthsShort")[month0];
}

/** "Pzt".."Paz" / "Mon".."Sun" - Monday-first, matching `monthMatrix`'s columns. */
export function weekdaysShort(): string[] {
  return calendarNames("weekdaysShort");
}

const DAYS_IN_WEEK = 7;
const GRID_ROWS = 6;
const MS_PER_DAY = 86_400_000;

function pad2(value: number): string {
  return String(value).padStart(2, "0");
}

export function toDateOnly(year: number, month0: number, day: number): string {
  return `${year}-${pad2(month0 + 1)}-${pad2(day)}`;
}

export function parseDateOnly(value: string): { year: number; month0: number; day: number } {
  const [year, month, day] = value.split("-").map(Number);
  return { year, month0: month - 1, day };
}

/** Calendar-day count between two dates, both ends included ("2026-01-11".."2026-01-23" → 13).
 * `Date.UTC` is only used as a DST-free day counter here, never to convert a local date. */
export function daysInclusive(from: string, to: string): number {
  const a = parseDateOnly(from);
  const b = parseDateOnly(to);
  const diff = Date.UTC(b.year, b.month0, b.day) - Date.UTC(a.year, a.month0, a.day);
  return Math.round(diff / MS_PER_DAY) + 1;
}

/** A 6x7 Monday-first grid of `DateOnly` strings for the month; cells outside it are `null`. */
export function monthMatrix(year: number, month0: number): (string | null)[][] {
  const firstWeekday = new Date(year, month0, 1).getDay(); // 0 = Sunday
  const leading = (firstWeekday + 6) % DAYS_IN_WEEK; // Monday-first offset
  const daysInMonth = new Date(year, month0 + 1, 0).getDate();

  const rows: (string | null)[][] = [];
  for (let row = 0; row < GRID_ROWS; row++) {
    const cells: (string | null)[] = [];
    for (let column = 0; column < DAYS_IN_WEEK; column++) {
      const day = row * DAYS_IN_WEEK + column - leading + 1;
      cells.push(day >= 1 && day <= daysInMonth ? toDateOnly(year, month0, day) : null);
    }
    rows.push(cells);
  }
  return rows;
}

/**
 * One tap on a calendar day. Single mode picks that day as a one-day range. Range mode starts a new
 * range when nothing (or a complete range) is selected, otherwise completes it - swapping the ends
 * when the second tap is before the first.
 */
export function applyDayTap(
  state: { from?: string; to?: string; mode: DateRangeMode },
  day: string
): { from?: string; to?: string } {
  if (state.mode === "single") return { from: day, to: day };
  if (!state.from || state.to) return { from: day, to: undefined };
  return day < state.from ? { from: day, to: state.from } : { from: state.from, to: day };
}

/** Inclusive range check; `false` while the range has no end yet. */
export function isDayInRange(day: string, from?: string, to?: string): boolean {
  if (!from || !to) return false;
  return day >= from && day <= to;
}

export function shiftMonth(
  year: number,
  month0: number,
  delta: number
): { year: number; month0: number } {
  const total = year * 12 + month0 + delta;
  return { year: Math.floor(total / 12), month0: ((total % 12) + 12) % 12 };
}

/** "Eylül 2026" */
export function formatMonthYear(year: number, month0: number): string {
  return `${monthName(month0)} ${year}`;
}

/** "29 Eyl" */
export function formatDayMonth(value: string): string {
  const { month0, day } = parseDateOnly(value);
  return `${day} ${monthNameShort(month0)}`;
}

/** "29 Eyl 2026" */
export function formatDayMonthYear(value: string): string {
  return `${formatDayMonth(value)} ${parseDateOnly(value).year}`;
}

/** "29 Eyl → 3 Eki", or just "29 Eyl" for a one-day range. */
export function formatShortRange(from: string, to: string): string {
  return from === to ? formatDayMonth(from) : `${formatDayMonth(from)} → ${formatDayMonth(to)}`;
}
