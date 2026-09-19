/**
 * Quick Input amount parser (senseik-035): recognizes "<number> TL/lira" style amounts inside free text.
 * Pure/local — no network calls. Only Turkish lira is recognized (the pilot form only ever posts `currency: "TRY"`);
 * anything else, or a number with no currency marker, returns `null` rather than guessing.
 */

export interface ParsedAmount {
  amount: number;
  currency: string;
}

const CURRENCY_WORD = "tl|try|₺|lira";
const AMOUNT_RE = new RegExp(`(\\d{1,3}(?:[.,]\\d{3})*(?:[.,]\\d{1,2})?|\\d+(?:[.,]\\d{1,2})?)\\s*(?:${CURRENCY_WORD})\\b`, "i");

/** Turkish/European-style grouped numbers ("1.250,75") as well as a single plain decimal ("450.50" or "450,50"). */
function toNumber(raw: string): number | null {
  const hasDot = raw.includes(".");
  const hasComma = raw.includes(",");
  let normalized: string;

  if (hasDot && hasComma) {
    const lastDot = raw.lastIndexOf(".");
    const lastComma = raw.lastIndexOf(",");
    normalized = lastComma > lastDot ? raw.replace(/\./g, "").replace(",", ".") : raw.replace(/,/g, "");
  } else if (hasComma) {
    const decimals = raw.slice(raw.lastIndexOf(",") + 1);
    normalized = decimals.length <= 2 ? raw.replace(",", ".") : raw.replace(/,/g, "");
  } else if (hasDot) {
    const decimals = raw.slice(raw.lastIndexOf(".") + 1);
    normalized = decimals.length <= 2 ? raw : raw.replace(/\./g, "");
  } else {
    normalized = raw;
  }

  const value = Number(normalized);
  return Number.isFinite(value) ? value : null;
}

export interface AmountMatch extends ParsedAmount {
  /** The matched phrase's span in `text`, for callers that need to strip it out. */
  start: number;
  end: number;
}

/**
 * Finds a single amount + currency in free text along with the span it was read from, e.g. "450 TL", "450,50 TL",
 * "450.50TL", "1.250 TL", "300 lira". Returns `null` when no number is followed by a recognized currency marker
 * (never guesses a bare number's currency).
 */
export function findAmountMatch(text: string): AmountMatch | null {
  const match = AMOUNT_RE.exec(text);
  if (!match) return null;

  const amount = toNumber(match[1]);
  if (amount === null || amount <= 0) return null;

  return { amount, currency: "TRY", start: match.index, end: match.index + match[0].length };
}

/** Extracts a single amount + currency from free text (see {@link findAmountMatch}). Returns `null` when nothing matches. */
export function parseAmount(text: string): ParsedAmount | null {
  const match = findAmountMatch(text);
  return match ? { amount: match.amount, currency: match.currency } : null;
}
