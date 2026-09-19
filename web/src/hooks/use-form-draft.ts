import { useEffect, useRef } from "react";

/** localStorage may throw (private mode / disabled storage); draft recovery must not break the form. */
function safeRead(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function safeWrite(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Draft is best-effort; the form still works without it.
  }
}

function safeRemove(key: string): void {
  try {
    localStorage.removeItem(key);
  } catch {
    // Nothing to clear.
  }
}

const DEFAULT_DEBOUNCE_MS = 800;

export interface UseFormDraftOptions<T> {
  /** localStorage key; prefix with a form/module name to avoid collisions, e.g. "senseik.draft.employee-create". */
  storageKey: string;
  /** Current form values; saved (debounced) to `storageKey` while `enabled`. */
  value: T;
  /** Only persist/restore while true - typically the dialog/page's own "open" state, so a form that
   * never unmounts (e.g. a Mantine `Modal` kept mounted for its close transition) doesn't keep
   * writing after it's closed, and restores again each time it's reopened. */
  enabled: boolean;
  /** Called once whenever `enabled` turns true and a stored draft exists - apply it back onto form state. */
  onRestore: (draft: T) => void;
  /** Skip persisting (and clear any existing draft for) values that don't need recovering, e.g. an all-blank form. */
  isEmpty?: (value: T) => boolean;
  /** Debounce window between an edit and the localStorage write (default 800ms). */
  debounceMs?: number;
}

export interface UseFormDraftResult {
  /** Call after a successful submit so the draft doesn't reappear next time the form opens. */
  clearDraft: () => void;
}

/**
 * Generic localStorage-backed draft recovery for long forms: debounced autosave while the form is
 * open, restored on open, cleared after a successful submit.
 *
 * Exists because a session-expiry redirect (api-client's 401 handler, see
 * web/src/lib/http-interceptors.ts) is a hard `window.location.assign("/login")` - any in-progress
 * form state not yet submitted is otherwise lost with no way back (S-53). This hook is form-agnostic
 * on purpose: wire it into any long form by passing its own values object and setters.
 */
export function useFormDraft<T>({
  storageKey,
  value,
  enabled,
  onRestore,
  isEmpty,
  debounceMs = DEFAULT_DEBOUNCE_MS,
}: UseFormDraftOptions<T>): UseFormDraftResult {
  // Kept in sync via an effect (never written during render) so the restore effect below can call
  // the latest `onRestore` without needing it in its own dependency array.
  const onRestoreRef = useRef(onRestore);
  useEffect(() => {
    onRestoreRef.current = onRestore;
  });

  // Restore once per "open" transition (not on every render / value change / onRestore identity change).
  useEffect(() => {
    if (!enabled) return;
    const raw = safeRead(storageKey);
    if (!raw) return;
    try {
      onRestoreRef.current(JSON.parse(raw) as T);
    } catch {
      // Corrupt/stale draft (e.g. an old shape after a form redesign) - drop it rather than throw.
      safeRemove(storageKey);
    }
  }, [enabled, storageKey]);

  // Debounced autosave while open.
  useEffect(() => {
    if (!enabled) return;
    const timer = setTimeout(() => {
      if (isEmpty?.(value)) {
        safeRemove(storageKey);
      } else {
        safeWrite(storageKey, JSON.stringify(value));
      }
    }, debounceMs);
    return () => clearTimeout(timer);
  }, [enabled, value, storageKey, debounceMs, isEmpty]);

  return {
    clearDraft: () => safeRemove(storageKey),
  };
}
