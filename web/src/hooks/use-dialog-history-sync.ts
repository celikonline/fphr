import { useSearchParams } from "react-router";

/** Search param key used to mark which form/dialog is open (senseik-022); exported so a caller
 * that needs to fold this into a single atomic `setSearchParams` call (e.g. alongside deleting a
 * different, unrelated param on mount) can reuse the same key instead of duplicating the string. */
export const DIALOG_SEARCH_PARAM = "dialog";

/**
 * Ties a form/dialog's open state to `?dialog=<name>` in the URL (senseik-022): opening pushes a
 * history entry, closing removes the marker in place (`replace`) so it never litters the forward
 * stack. Because `isOpen` is read straight from the URL, the browser back button closes the
 * dialog for free — popping the pushed entry removes the marker, `isOpen` flips to `false`, and
 * the caller's `<Modal opened={isOpen}>` (or a render gate `{isOpen && <Dialog />}`) reacts to
 * that like any other prop change. Real route navigations elsewhere are unaffected: they don't
 * touch this param.
 *
 * Callers that keep richer local state (which mode, which record) still own that state — call
 * `open()`/`close()` alongside it, and clear the local state in an effect that watches `isOpen`
 * so a back-button close (no local `close()` call happened) still resets it:
 *
 * ```tsx
 * const { isOpen, open, close } = useDialogHistorySync("leave-request");
 * useEffect(() => { if (!isOpen && dialog) setDialog(null); }, [isOpen, dialog]);
 * ```
 */
export function useDialogHistorySync(name: string) {
  const [searchParams, setSearchParams] = useSearchParams();
  const isOpen = searchParams.get(DIALOG_SEARCH_PARAM) === name;

  function open() {
    if (isOpen) return;
    setSearchParams((params) => {
      const next = new URLSearchParams(params);
      next.set(DIALOG_SEARCH_PARAM, name);
      return next;
    });
  }

  function close() {
    if (searchParams.get(DIALOG_SEARCH_PARAM) !== name) return;
    setSearchParams(
      (params) => {
        const next = new URLSearchParams(params);
        next.delete(DIALOG_SEARCH_PARAM);
        return next;
      },
      { replace: true },
    );
  }

  return { isOpen, open, close } as const;
}
