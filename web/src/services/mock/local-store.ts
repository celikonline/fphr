/**
 * Tiny localStorage-backed persistence helper for services that mock a backend
 * that doesn't exist yet (see the per-service TODO comments for which ones).
 *
 * Using localStorage instead of a plain in-memory module variable means the
 * demo data survives a page refresh, which matters for manually validating a
 * "Save" flow. None of this is used once the real endpoint exists - callers
 * only need to change the implementation of the exported service functions.
 */
export function readMock<T>(key: string, fallback: T): T {
  try {
    const raw = window.localStorage.getItem(key);
    return raw ? (JSON.parse(raw) as T) : fallback;
  } catch {
    return fallback;
  }
}

export function writeMock<T>(key: string, value: T): T {
  try {
    window.localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Storage unavailable (private mode / quota) - the in-memory value is still returned.
  }
  return value;
}

/** Simulates realistic network latency so loading states are visible during manual QA. */
export function mockDelay(ms = 250): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

let seq = 1000;
export function nextMockId(prefix: string): string {
  seq += 1;
  return `${prefix}-${seq}-${Date.now().toString(36)}`;
}
