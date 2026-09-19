import { describe, expect, it } from "vitest";
import { waitFor } from "@testing-library/react";
import { useAuthStore } from "./auth.store";

// senseik-042: `hasHydrated` is what lets ProtectedRoute tell "no session yet" apart from "session
// not restored yet" - if the `onRehydrateStorage` wiring that flips it is ever removed or broken,
// the store would stay `hasHydrated: false` forever and every authenticated page would hang on the
// loading screen. This only asserts the flag eventually settles to `true` on its own; it does not
// pin the exact tick it happens on, since that is an implementation detail of zustand's `persist`.
describe("auth store hydration (senseik-042)", () => {
  it("flips hasHydrated to true once the persist middleware has read storage", async () => {
    await waitFor(() => expect(useAuthStore.getState().hasHydrated).toBe(true));
  });
});
