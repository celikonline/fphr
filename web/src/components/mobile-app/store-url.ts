/**
 * Validates a store URL read from env config (`VITE_IOS_APP_URL` / `VITE_ANDROID_APP_URL`): trims
 * whitespace and requires an `https://` scheme. Returns null when the value is absent, blank, or not
 * a valid https URL - callers render the store button as disabled ("Yakında") in that case.
 */
export function storeUrl(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const trimmed = value.trim();
  return trimmed.startsWith("https://") ? trimmed : null;
}
