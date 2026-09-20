import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { Button, MantineProvider } from "@mantine/core";
import { usePermission } from "@/components/permission-guard";
import { useAuthStore } from "@/store/auth.store";
import ProtectedRoute from "./protected-route";

// senseik-042 root cause: `user`/`features` (and therefore every usePermission/useAccessSetting
// check) only exist in the store after zustand's `persist` middleware restores them from storage,
// which happens a tick after the store is created - not before the first render. Rendering the
// authenticated shell during that gap makes every permission-gated action button ("+ Talep", "Yeni
// çalışan ekle", "Düzenle", ...) silently absent from the DOM: a click aimed at where the button
// belongs lands on nothing and only a retry, once hydration has caught up, appears to "work".
// ProtectedRoute must hold the shell back until `hasHydrated` is true so the buttons are either
// fully absent (redirect to /login) or fully present - never a half-populated flash in between.

// jsdom has no matchMedia; MantineProvider's color-scheme detection needs one to render.
beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }),
  });
});

/** `isAuthenticated()` also checks the JWT `exp` claim - a plain string like "test-token" decodes to
 * nothing and reads as expired, so tests that need an authenticated session need a real-shaped token. */
function fakeJwt(expiresInSeconds = 3600): string {
  const header = btoa(JSON.stringify({ alg: "none" }));
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + expiresInSeconds }));
  return `${header}.${payload}.signature`;
}

function ActionButton({ onClick }: { onClick: () => void }) {
  const canCreate = usePermission("employee.employees.create");
  return canCreate ? (
    <Button type="button" onClick={onClick} data-testid="create-button">
      Yeni çalışan ekle
    </Button>
  ) : null;
}

function renderProtected(onClick: () => void = vi.fn()) {
  return render(
    <MantineProvider>
      <MemoryRouter initialEntries={["/app"]}>
        <Routes>
          <Route path="/login" element={<div>Login page</div>} />
          <Route
            path="/app"
            element={
              <ProtectedRoute>
                <ActionButton onClick={onClick} />
              </ProtectedRoute>
            }
          />
        </Routes>
      </MemoryRouter>
    </MantineProvider>,
  );
}

describe("ProtectedRoute hydration gate (senseik-042)", () => {
  it("shows a loading state instead of a half-populated shell before the store has hydrated", () => {
    useAuthStore.setState({
      hasHydrated: false,
      token: fakeJwt(),
      user: { id: "u1", email: "a@b.com", name: "A", roles: [], roleLabels: [], permissions: ["employee.employees.create"] },
      features: null,
    });

    renderProtected();

    // Neither the gated button nor a premature login redirect - just a neutral loading state.
    expect(screen.queryByTestId("create-button")).toBeNull();
    expect(screen.queryByText("Login page")).toBeNull();
    expect(screen.getByTestId("protected-route-loading")).toBeInTheDocument();
  });

  it("redirects to /login when hydration finishes and there is no session", () => {
    useAuthStore.setState({ hasHydrated: true, token: null, user: null, features: null });

    renderProtected();

    expect(screen.getByText("Login page")).toBeInTheDocument();
    expect(screen.queryByTestId("create-button")).toBeNull();
  });

  it("renders the gated action already present once hydrated, and it answers the very first rapid click", async () => {
    useAuthStore.setState({
      hasHydrated: true,
      token: fakeJwt(),
      user: { id: "u1", email: "a@b.com", name: "A", roles: [], roleLabels: [], permissions: ["employee.employees.create"] },
      features: null,
    });

    const onClick = vi.fn();
    renderProtected(onClick);

    // Present on the very first render - no pop-in to race a click against.
    const button = screen.getByTestId("create-button");

    const user = userEvent.setup();
    // Two clicks fired back-to-back: both must register once the shell has committed. Before the
    // hydration gate existed, the equivalent first click could land while the button didn't exist yet.
    await user.click(button);
    await user.click(button);

    expect(onClick).toHaveBeenCalledTimes(2);
  });
});
