import { describe, expect, it } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { MemoryRouter, Routes, Route, useNavigate } from "react-router";
import { useDialogHistorySync } from "./use-dialog-history-sync";

// senseik-022: form/dialog open state should follow the URL (`?dialog=<name>`) so the browser
// back button closes it instead of leaving the page. This hook is the shared sync point every
// URL-aware dialog (leave request, create employee, ...) is built on.

function makeWrapper() {
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return (
      <MemoryRouter initialEntries={["/app/leave/me"]}>
        <Routes>
          <Route path="/app/leave/me" element={children} />
        </Routes>
      </MemoryRouter>
    );
  };
}

describe("useDialogHistorySync", () => {
  it("starts closed when the URL has no dialog param", () => {
    const { result } = renderHook(() => useDialogHistorySync("leave-request"), { wrapper: makeWrapper() });
    expect(result.current.isOpen).toBe(false);
  });

  it("open() sets ?dialog=<name> and flips isOpen to true", () => {
    const { result } = renderHook(() => useDialogHistorySync("leave-request"), { wrapper: makeWrapper() });

    act(() => result.current.open());

    expect(result.current.isOpen).toBe(true);
  });

  it("close() removes the dialog param and flips isOpen back to false", () => {
    const { result } = renderHook(() => useDialogHistorySync("leave-request"), { wrapper: makeWrapper() });

    act(() => result.current.open());
    expect(result.current.isOpen).toBe(true);

    act(() => result.current.close());
    expect(result.current.isOpen).toBe(false);
  });

  it("open() pushes a history entry that a back navigation pops (closing the dialog)", () => {
    let goBack: (() => void) | null = null;
    function BackTrigger() {
      const navigate = useNavigate();
      goBack = () => navigate(-1);
      return null;
    }

    const { result } = renderHook(() => useDialogHistorySync("leave-request"), {
      wrapper: ({ children }) => (
        <MemoryRouter initialEntries={["/app/leave/me"]}>
          <Routes>
            <Route
              path="/app/leave/me"
              element={
                <>
                  {children}
                  <BackTrigger />
                </>
              }
            />
          </Routes>
        </MemoryRouter>
      ),
    });

    act(() => result.current.open());
    expect(result.current.isOpen).toBe(true);

    // Simulate the browser back button: pop the history entry `open()` pushed.
    act(() => goBack?.());

    expect(result.current.isOpen).toBe(false);
  });

  it("does not confuse a differently-named dialog open on the same page", () => {
    function useBothDialogs() {
      return {
        leave: useDialogHistorySync("leave-request"),
        employee: useDialogHistorySync("create-employee"),
      };
    }

    const { result } = renderHook(() => useBothDialogs(), { wrapper: makeWrapper() });

    act(() => result.current.leave.open());

    expect(result.current.leave.isOpen).toBe(true);
    expect(result.current.employee.isOpen).toBe(false);
  });
});
