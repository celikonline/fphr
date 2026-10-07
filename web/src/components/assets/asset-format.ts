import { formatDate, formatDateOnly } from "@/lib/dates";

/** `acknowledgedAt` is documented as a moment; accept a plain "yyyy-MM-dd" too so the day never shifts. */
export function formatMoment(value: string, locale: string): string {
  return /^\d{4}-\d{2}-\d{2}$/.test(value)
    ? formatDateOnly(value, locale)
    : formatDate(value, locale);
}

/** Purchase cost as a plain locale-formatted number - the currency is the tenant default and is not shown. */
export function formatCost(value: number, locale: string): string {
  return new Intl.NumberFormat(locale, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value);
}

/** Decimal separator of the locale, for number inputs that must accept what the user is used to typing. */
export function decimalSeparatorOf(locale: string): "." | "," {
  const part = new Intl.NumberFormat(locale).formatToParts(1.1).find((p) => p.type === "decimal");
  return part?.value === "," ? "," : ".";
}
