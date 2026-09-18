/**
 * Quick Input keyword parser (senseik-035): matches free text against a `{ category: [phrase, ...] }` dictionary
 * and returns the best (most specific) matching category key, or `null` when nothing matches.
 * Pure/local — no network calls, no fuzzy/ML matching, fully deterministic.
 */

/** JS `\b` does not treat Turkish letters (ç ğ ı ö ş ü) as word characters, so phrase boundaries are checked manually. */
const WORD_CHAR_RE = /[a-z0-9çğıöşü]/;

function isBoundary(text: string, index: number): boolean {
  if (index < 0 || index >= text.length) return true;
  return !WORD_CHAR_RE.test(text[index]);
}

export interface KeywordMatch {
  key: string;
  /** The matched phrase's span in the (Turkish-lowercased) input, for callers that need to strip it out. */
  start: number;
  end: number;
}

function findPhraseMatch(lowerText: string, phrase: string): { start: number; end: number } | null {
  let fromIndex = 0;
  while (fromIndex <= lowerText.length - phrase.length) {
    const index = lowerText.indexOf(phrase, fromIndex);
    if (index === -1) return null;
    if (isBoundary(lowerText, index - 1) && isBoundary(lowerText, index + phrase.length)) {
      return { start: index, end: index + phrase.length };
    }
    fromIndex = index + 1;
  }
  return null;
}

/**
 * Finds the best dictionary match in `text`. "Best" = the longest matching phrase (most specific wins, e.g.
 * "akşam yemeği" over "yemek"); ties break to the match that starts earliest. Returns `null` when no phrase matches.
 */
export function findKeywordMatch(text: string, dictionary: Record<string, string[]>): KeywordMatch | null {
  const lower = text.toLocaleLowerCase("tr-TR");
  let best: KeywordMatch | null = null;

  for (const [key, phrases] of Object.entries(dictionary)) {
    for (const phrase of phrases) {
      const normalizedPhrase = phrase.toLocaleLowerCase("tr-TR").trim();
      if (!normalizedPhrase) continue;
      const match = findPhraseMatch(lower, normalizedPhrase);
      if (!match) continue;
      const length = normalizedPhrase.length;
      const isBetter = !best || length > best.end - best.start || (length === best.end - best.start && match.start < best.start);
      if (isBetter) best = { key, start: match.start, end: match.end };
    }
  }

  return best;
}

/** Returns just the matched category key (see {@link findKeywordMatch}), or `null` when nothing matches. */
export function parseKeyword(text: string, dictionary: Record<string, string[]>): string | null {
  return findKeywordMatch(text, dictionary)?.key ?? null;
}
