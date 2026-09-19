import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook } from "@testing-library/react";
import { useFormDraft } from "./use-form-draft";

const STORAGE_KEY = "senseik.draft.test-form";

interface DraftShape {
  name: string;
}

describe("useFormDraft", () => {
  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("writes the value to localStorage after the debounce window while enabled", () => {
    const onRestore = vi.fn();
    const { rerender } = renderHook(
      ({ value }: { value: DraftShape }) =>
        useFormDraft<DraftShape>({ storageKey: STORAGE_KEY, value, enabled: true, onRestore, debounceMs: 500 }),
      { initialProps: { value: { name: "" } } },
    );

    rerender({ value: { name: "Ada" } });
    // Not yet written - still inside the debounce window.
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();

    vi.advanceTimersByTime(500);
    expect(localStorage.getItem(STORAGE_KEY)).toBe(JSON.stringify({ name: "Ada" }));
  });

  it("does not persist or restore while disabled", () => {
    const onRestore = vi.fn();
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ name: "Existing" }));

    const { rerender } = renderHook(
      ({ value, enabled }: { value: DraftShape; enabled: boolean }) =>
        useFormDraft<DraftShape>({ storageKey: STORAGE_KEY, value, enabled, onRestore, debounceMs: 500 }),
      { initialProps: { value: { name: "" }, enabled: false } },
    );

    expect(onRestore).not.toHaveBeenCalled();

    rerender({ value: { name: "Changed" }, enabled: false });
    vi.advanceTimersByTime(1000);
    // Still the pre-existing draft - a closed/disabled form must not overwrite it.
    expect(localStorage.getItem(STORAGE_KEY)).toBe(JSON.stringify({ name: "Existing" }));
  });

  it("restores a stored draft once the form transitions to enabled (form opened)", () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ name: "Restored draft" }));
    const onRestore = vi.fn();

    const { rerender } = renderHook(
      ({ enabled }: { enabled: boolean }) =>
        useFormDraft<DraftShape>({ storageKey: STORAGE_KEY, value: { name: "" }, enabled, onRestore }),
      { initialProps: { enabled: false } },
    );
    expect(onRestore).not.toHaveBeenCalled();

    rerender({ enabled: true });
    expect(onRestore).toHaveBeenCalledWith({ name: "Restored draft" });
  });

  it("clearDraft removes the stored draft (called after a successful submit)", () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ name: "Ada" }));
    const onRestore = vi.fn();

    const { result } = renderHook(() =>
      useFormDraft<DraftShape>({ storageKey: STORAGE_KEY, value: { name: "Ada" }, enabled: true, onRestore }),
    );

    result.current.clearDraft();
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it("removes an existing draft instead of writing once the value becomes empty", () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ name: "Ada" }));
    const onRestore = vi.fn();
    const isEmpty = (value: DraftShape) => value.name.trim() === "";

    const { rerender } = renderHook(
      ({ value }: { value: DraftShape }) =>
        useFormDraft<DraftShape>({ storageKey: STORAGE_KEY, value, enabled: true, onRestore, isEmpty, debounceMs: 300 }),
      { initialProps: { value: { name: "Ada" } } },
    );

    rerender({ value: { name: "" } });
    vi.advanceTimersByTime(300);
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it("drops a corrupt stored draft instead of throwing", () => {
    localStorage.setItem(STORAGE_KEY, "{not-json");
    const onRestore = vi.fn();

    expect(() =>
      renderHook(() => useFormDraft<DraftShape>({ storageKey: STORAGE_KEY, value: { name: "" }, enabled: true, onRestore })),
    ).not.toThrow();

    expect(onRestore).not.toHaveBeenCalled();
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });
});
