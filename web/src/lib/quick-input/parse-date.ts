/**
 * Quick Input date parser (senseik-035): recognizes a handful of Turkish date expressions inside free text.
 * Pure/local — no network calls, no dictionaries beyond the fixed month-name table below (KVKK: nothing leaves the browser).
 * Returns `null` whenever the text is ambiguous or unrecognized; callers must never guess a date from a `null`.
 */

const ONE_DAY_MS = 24 * 60 * 60 * 1000;

const TURKISH_MONTHS: Record<string, number> = {
  ocak: 0,
  şubat: 1,
  subat: 1,
  mart: 2,
  nisan: 3,
  mayıs: 4,
  mayis: 4,
  haziran: 5,
  temmuz: 6,
  ağustos: 7,
  agustos: 7,
  eylül: 8,
  eylul: 8,
  ekim: 9,
  kasım: 10,
  kasim: 10,
  aralık: 11,
  aralik: 11,
};

const MONTH_NAME_PATTERN = Object.keys(TURKISH_MONTHS).join("|");
const NAMED_DATE_RE = new RegExp(`\\b(\\d{1,2})\\s+(${MONTH_NAME_PATTERN})\\b(?:\\s+(\\d{4}))?`);
const NUMERIC_DATE_RE = /\b(\d{1,2})[./](\d{1,2})[./](\d{4}|\d{2})\b/;

function toTurkishLowerCase(value: string): string {
  return value.toLocaleLowerCase("tr-TR");
}

function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

function addDays(date: Date, days: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days);
}

/** Builds a calendar date and rejects overflowed inputs (e.g. 31 Şubat) instead of letting `Date` roll them over. */
function makeStrictDate(year: number, monthIndex: number, day: number): Date | null {
  if (monthIndex < 0 || monthIndex > 11 || day < 1 || day > 31) return null;
  const date = new Date(year, monthIndex, day);
  if (date.getFullYear() !== year || date.getMonth() !== monthIndex || date.getDate() !== day) return null;
  return date;
}

export interface DateMatch {
  date: Date;
  /** The matched phrase's span in the (Turkish-lowercased) input, for callers that need to strip it out. */
  start: number;
  end: number;
}

const RELATIVE_PATTERNS: Array<{ re: RegExp; toDate: (today: Date) => Date }> = [
  { re: /\bbug[üu]n\b/, toDate: (today) => today },
  { re: /\bd[üu]n\b/, toDate: (today) => addDays(today, -1) },
  { re: /\byar[ıi]n\b/, toDate: (today) => addDays(today, 1) },
  { re: /\bge[çc]en hafta\b/, toDate: (today) => addDays(today, -7) },
];

function findRelativeMatch(lower: string, today: Date): DateMatch | null {
  for (const { re, toDate } of RELATIVE_PATTERNS) {
    const match = re.exec(lower);
    if (match) return { date: toDate(today), start: match.index, end: match.index + match[0].length };
  }
  return null;
}

function findNumericMatch(lower: string): DateMatch | null {
  const match = NUMERIC_DATE_RE.exec(lower);
  if (!match) return null;
  const day = Number(match[1]);
  const month = Number(match[2]);
  const year = match[3].length === 2 ? 2000 + Number(match[3]) : Number(match[3]);
  const date = makeStrictDate(year, month - 1, day);
  return date ? { date, start: match.index, end: match.index + match[0].length } : null;
}

/** "20 Ocak 2026" or "20 Ocak" (year omitted defaults to the current year, rolled back one year if that lands in the future). */
function findNamedMonthMatch(lower: string, today: Date): DateMatch | null {
  const match = NAMED_DATE_RE.exec(lower);
  if (!match) return null;
  const day = Number(match[1]);
  const monthIndex = TURKISH_MONTHS[match[2]];
  const explicitYear = match[3] ? Number(match[3]) : null;
  const year = explicitYear ?? today.getFullYear();
  let date = makeStrictDate(year, monthIndex, day);
  if (!date) return null;
  if (explicitYear === null && date.getTime() > today.getTime() + ONE_DAY_MS) {
    // Expense receipts are dated in the past; an unqualified month/day in the future almost always means "last year".
    date = makeStrictDate(year - 1, monthIndex, day);
    if (!date) return null;
  }
  return { date, start: match.index, end: match.index + match[0].length };
}

/**
 * Finds a single date in free text along with the span it was read from. Recognizes: "dün", "bugün", "yarın",
 * "geçen hafta", "20 Ocak", "20 Ocak 2026", "20.01.2026", "20/01/2026". Returns `null` when nothing matches.
 */
export function findDateMatch(text: string, referenceDate: Date = new Date()): DateMatch | null {
  const lower = toTurkishLowerCase(text);
  const today = startOfDay(referenceDate);

  return findRelativeMatch(lower, today) ?? findNumericMatch(lower) ?? findNamedMonthMatch(lower, today);
}

/** Extracts a single date from free text (see {@link findDateMatch}). Returns `null` when nothing matches. */
export function parseDate(text: string, referenceDate: Date = new Date()): Date | null {
  return findDateMatch(text, referenceDate)?.date ?? null;
}
