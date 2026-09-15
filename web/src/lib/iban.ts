/**
 * IBAN helpers mirroring the backend (`Sense.Shared.Kernel.ValueObjects.Iban` + `BankAccount.ParseIban`): strip whitespace,
 * uppercase, ISO 13616 mod-97 checksum; only TR (26 characters, digits after the country code) is accepted for now.
 */

export type IbanProblem = "required" | "invalid" | "countryUnsupported";

const SUPPORTED_COUNTRIES = new Set(["TR"]);
const COUNTRY_LENGTHS: Record<string, number> = { TR: 26 };
const NUMERIC_BBAN_COUNTRIES = new Set(["TR"]);
const MIN_LENGTH = 15;
const MAX_LENGTH = 34;

export function normalizeIban(value: string): string {
  return value.replace(/\s+/g, "").toUpperCase();
}

/** "TR330006100519786457841326" → "TR33 0006 1005 1978 6457 8413 26" (normalizes first). */
export function formatIban(value: string): string {
  return normalizeIban(value).replace(/(.{4})/g, "$1 ").trim();
}

function mod97(value: string): number {
  let remainder = 0;
  for (const char of value) {
    const digits = /[0-9]/.test(char) ? char : String(char.charCodeAt(0) - 55);
    for (const digit of digits) {
      remainder = (remainder * 10 + Number(digit)) % 97;
    }
  }
  return remainder;
}

/** Well-formed IBAN of any country (length, characters, country length table, mod-97). */
export function isWellFormedIban(value: string): boolean {
  const iban = normalizeIban(value);
  if (iban.length < MIN_LENGTH || iban.length > MAX_LENGTH || !/^[A-Z]{2}[0-9]{2}[A-Z0-9]+$/.test(iban)) return false;
  const country = iban.slice(0, 2);
  if (COUNTRY_LENGTHS[country] !== undefined && iban.length !== COUNTRY_LENGTHS[country]) return false;
  if (NUMERIC_BBAN_COUNTRIES.has(country) && !/^[0-9]+$/.test(iban.slice(4))) return false;
  return mod97(iban.slice(4) + iban.slice(0, 4)) === 1;
}

/** Null when the IBAN would be accepted by the API; otherwise why not. */
export function validateIban(value: string): IbanProblem | null {
  const iban = normalizeIban(value);
  if (!iban) return "required";
  if (!isWellFormedIban(iban)) return "invalid";
  return SUPPORTED_COUNTRIES.has(iban.slice(0, 2)) ? null : "countryUnsupported";
}
