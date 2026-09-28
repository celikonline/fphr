import type { AccentKey } from "@/theme/theme";
import { toLocaleUpper } from "@/lib/formatters";

const ACCENT_ORDER: readonly AccentKey[] = [
  "blue",
  "green",
  "coral",
  "purple",
  "amber",
  "teal",
  "indigo",
  "pink",
  "gray",
];

/**
 * "Ali Şahin" -> "AŞ", "İlkay Şahin-Öztürk" -> "İŞ" (hyphenated surname counts as one word),
 * "Ece" -> "E", blank/empty -> "?".
 */
export function initialsOf(fullName: string): string {
  const words = fullName.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return "?";
  const initials = words.length === 1 ? words[0][0] : `${words[0][0]}${words[words.length - 1][0]}`;
  return toLocaleUpper(initials);
}

/** Deterministic (same name -> same accent every render) hash into the 9-color accent palette. */
export function accentForName(fullName: string): AccentKey {
  let hash = 0;
  for (let i = 0; i < fullName.length; i += 1) {
    hash = (hash * 31 + fullName.charCodeAt(i)) | 0;
  }
  return ACCENT_ORDER[Math.abs(hash) % ACCENT_ORDER.length];
}
