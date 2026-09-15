/** Formats an amount in its ISO 4217 currency for the UI language (falls back to "1.234,56 XYZ" for a code Intl rejects). */
export function formatMoney(amount: number, currency: string, locale: string): string {
  try {
    return new Intl.NumberFormat(locale, { style: "currency", currency, minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount);
  } catch {
    return `${new Intl.NumberFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount)} ${currency}`;
  }
}

/** At most two decimals (kuruş), matching the API's numeric(18,2) columns. */
export function hasAtMostTwoDecimals(amount: number): boolean {
  return Math.abs(Math.round(amount * 100) - amount * 100) < 1e-6;
}

/** Currencies offered first in money selects; any other active ISO 4217 code the API returns is shown as well. */
export const COMMON_CURRENCIES = ["TRY", "EUR", "USD", "GBP", "CHF", "JPY", "CNY", "RUB", "SAR", "AED"];
