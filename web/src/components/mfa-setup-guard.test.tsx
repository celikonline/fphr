import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import MfaSetupGuard from "./mfa-setup-guard";
import { useAuthStore } from "@/store/auth.store";

// senseik-051: MfaSetupGuard reads only `features.mfaSetupRequired` off the auth store (computed server-side by
// GetMeHandler from the tenant's security policy + the caller's roles/MFA state) - mirrors use-nav-visibility.test.ts.
vi.mock("@/store/auth.store", () => ({
  useAuthStore: vi.fn(),
}));

const mockUseAuthStore = useAuthStore as unknown as ReturnType<typeof vi.fn>;

function mockMfaSetupRequired(mfaSetupRequired: boolean) {
  mockUseAuthStore.mockImplementation((selector: (state: unknown) => unknown) =>
    selector({ features: { mfaSetupRequired } })
  );
}

function renderAt(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route element={<MfaSetupGuard />}>
          <Route path="/app/employees" element={<div>Employees page</div>} />
          <Route path="/app/account" element={<div>Account page</div>} />
        </Route>
      </Routes>
    </MemoryRouter>
  );
}

describe("MfaSetupGuard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("redirects any other screen to the account page when MFA setup is required", () => {
    mockMfaSetupRequired(true);

    renderAt("/app/employees");

    expect(screen.getByText("Account page")).not.toBeNull();
    expect(screen.queryByText("Employees page")).toBeNull();
  });

  it("does not redirect once the caller is already on the account page", () => {
    mockMfaSetupRequired(true);

    renderAt("/app/account");

    expect(screen.getByText("Account page")).not.toBeNull();
  });

  it("renders the requested page when MFA setup is not required", () => {
    mockMfaSetupRequired(false);

    renderAt("/app/employees");

    expect(screen.getByText("Employees page")).not.toBeNull();
  });

  it("treats a missing features block (not yet loaded) as not required", () => {
    mockUseAuthStore.mockImplementation((selector: (state: unknown) => unknown) => selector({ features: null }));

    renderAt("/app/employees");

    expect(screen.getByText("Employees page")).not.toBeNull();
  });
});
