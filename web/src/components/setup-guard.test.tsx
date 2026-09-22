import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import SetupGuard, { isSetupPending } from "./setup-guard";
import { useAuthStore, type User } from "@/store/auth.store";

// Same approach as mfa-setup-guard.test.tsx: SetupGuard only reads slices of the auth store, so the hook is stubbed with a selector.
vi.mock("@/store/auth.store", () => ({
  useAuthStore: vi.fn(),
}));

const mockUseAuthStore = useAuthStore as unknown as ReturnType<typeof vi.fn>;

function userWith(permissions: string[], setupCompleted: boolean | undefined): User {
  return {
    id: "u1",
    email: "boss@acme.test",
    name: "Boss",
    roles: [],
    roleLabels: [],
    permissions,
    tenant: { id: "t1", name: "Acme", slug: "acme", setupCompleted },
  };
}

/** A JWT-shaped token whose payload carries the `act` claim (support-access / impersonation session). */
function impersonationToken(): string {
  return `h.${btoa(JSON.stringify({ act: "platform-user" }))}.s`;
}

interface StoreOptions {
  user: User | null;
  mfaSetupRequired?: boolean;
  token?: string | null;
}

function mockStore({ user, mfaSetupRequired = false, token = null }: StoreOptions) {
  mockUseAuthStore.mockImplementation((selector: (state: unknown) => unknown) =>
    selector({
      user,
      token,
      features: { mfaSetupRequired, accessSettingsBypass: true, access: {} },
    })
  );
}

function renderAt(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route element={<SetupGuard />}>
          <Route path="/app" element={<div>Home page</div>} />
          <Route path="/app/employees" element={<div>Employees page</div>} />
          <Route path="/app/setup" element={<div>Setup wizard</div>} />
          <Route path="/app/account" element={<div>Account page</div>} />
        </Route>
      </Routes>
    </MemoryRouter>
  );
}

const OWNER_PERMISSIONS = ["identity.tenant.manage", "employee.employees.read"];

describe("SetupGuard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("sends an owner whose company setup is pending to the wizard from any screen", () => {
    mockStore({ user: userWith(OWNER_PERMISSIONS, false) });

    renderAt("/app/employees");

    expect(screen.getByText("Setup wizard")).not.toBeNull();
    expect(screen.queryByText("Employees page")).toBeNull();
  });

  it("leaves the wizard itself alone (no redirect loop)", () => {
    mockStore({ user: userWith(OWNER_PERMISSIONS, false) });

    renderAt("/app/setup");

    expect(screen.getByText("Setup wizard")).not.toBeNull();
  });

  it("lets the app through once setup is completed", () => {
    mockStore({ user: userWith(OWNER_PERMISSIONS, true) });

    renderAt("/app/employees");

    expect(screen.getByText("Employees page")).not.toBeNull();
  });

  it("treats a backend without the setupCompleted field as done", () => {
    mockStore({ user: userWith(OWNER_PERMISSIONS, undefined) });

    renderAt("/app/employees");

    expect(screen.getByText("Employees page")).not.toBeNull();
  });

  it("does not trap users who cannot manage the tenant, even while setup is pending", () => {
    mockStore({ user: userWith(["employee.employees.read"], false) });

    renderAt("/app/employees");

    expect(screen.getByText("Employees page")).not.toBeNull();
  });

  it("does not trap support staff working in an impersonation session", () => {
    mockStore({ user: userWith(OWNER_PERMISSIONS, false), token: impersonationToken() });

    renderAt("/app/employees");

    expect(screen.getByText("Employees page")).not.toBeNull();
  });

  it("lets the mandatory MFA setup win, so the account page and the wizard never bounce", () => {
    mockStore({ user: userWith(OWNER_PERMISSIONS, false), mfaSetupRequired: true });

    renderAt("/app/employees");

    expect(screen.getByText("Account page")).not.toBeNull();
  });
});

describe("isSetupPending", () => {
  it("is false without a user", () => {
    expect(isSetupPending(null, null)).toBe(false);
  });

  it("is true only for a tenant manager of a company whose setup is explicitly pending", () => {
    expect(isSetupPending(userWith(OWNER_PERMISSIONS, false), null)).toBe(true);
    expect(isSetupPending(userWith(OWNER_PERMISSIONS, true), null)).toBe(false);
    expect(isSetupPending(userWith(["employee.employees.read"], false), null)).toBe(false);
    expect(isSetupPending(userWith(OWNER_PERMISSIONS, false), "platform-user")).toBe(false);
  });
});
