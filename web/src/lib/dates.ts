/** Parses a backend `DateOnly` ("yyyy-MM-dd") as a local calendar date (no UTC shift). */
export function parseDateOnly(value: string): Date {
  const [year, month, day] = value.split("-").map(Number);
  return new Date(year, (month ?? 1) - 1, day ?? 1);
}

/** Formats a local calendar date as a backend `DateOnly` ("yyyy-MM-dd"). */
export function toDateOnly(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${date.getFullYear()}-${month}-${day}`;
}

export function formatDateOnly(value: string, locale: string, options: Intl.DateTimeFormatOptions = { dateStyle: "medium" }): string {
  return new Intl.DateTimeFormat(locale, options).format(parseDateOnly(value));
}

export function formatDateTime(value: string | null | undefined, locale: string): string {
  if (!value) return "—";
  return new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}

/** Whole days from today (local) to the given DateOnly; 0 = today. */
export function daysUntil(value: string): number {
  const today = new Date();
  const start = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  return Math.round((parseDateOnly(value).getTime() - start.getTime()) / 86_400_000);
}
