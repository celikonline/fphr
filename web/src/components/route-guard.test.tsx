import { beforeAll, describe, expect, it, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MantineProvider } from "@mantine/core";
import trCommon from "../../public/locales/tr/common.json";
import RouteGuard from "./route-guard";
import { useAuthStore } from "@/store/auth.store";
import { getRouteAccess } from "@/config/route-permissions";

// senseik-052: RouteGuard, menü filtrelemesiyle aynı hook'u (`useVisibleItems`) kullanıyor - bu yüzden
// auth store'u use-nav-visibility.test.ts'teki gibi (user.permissions + features) mockluyoruz.
// `route-permissions.ts` de mock'lanıyor ki testler navigation.ts'in gerçek içeriğinden bağımsız kalsın.
vi.mock("@/store/auth.store", () => ({
  useAuthStore: vi.fn(),
}));

vi.mock("@/config/route-permissions", () => ({
  getRouteAccess: vi.fn(),
}));

const mockUseAuthStore = useAuthStore as unknown as ReturnType<typeof vi.fn>;
const mockGetRouteAccess = getRouteAccess as unknown as ReturnType<typeof vi.fn>;

// jsdom has no matchMedia; MantineProvider's color-scheme detection and NoAccess's <Card> need one to render.
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

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "common",
    ns: ["common"],
    resources: { tr: { common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function mockAuthState(permissions: string[], features: Record<string, unknown> | null = { accessSettingsBypass: false, access: {} }) {
  mockUseAuthStore.mockImplementation((selector: (state: unknown) => unknown) =>
    selector({ user: { permissions }, features })
  );
}

function renderAt(initialPath: string) {
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <MantineProvider>
        <MemoryRouter initialEntries={[initialPath]}>
          <Routes>
            <Route element={<RouteGuard />}>
              <Route path="/app/settings/system/users-roles" element={<div>Users &amp; roles page</div>} />
              <Route path="/app/employees" element={<div>Employees page</div>} />
            </Route>
          </Routes>
        </MemoryRouter>
      </MantineProvider>
    </I18nextProvider>,
  );
}

describe("RouteGuard (senseik-052)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders the existing no-access screen instead of the page when the caller has none of the required permissions", () => {
    mockGetRouteAccess.mockReturnValue({ permissions: ["identity.users.view", "identity.roles.view"] });
    mockAuthState(["payroll.period.recalculate"]);

    renderAt("/app/settings/system/users-roles");

    expect(screen.queryByText("Users & roles page")).toBeNull();
    // Same "Bu sayfaya erişiminiz yok" screen every other permission-gated page already uses (no-access.tsx).
    expect(screen.getByText(trCommon.noAccessTitle)).toBeInTheDocument();
  });

  it("renders the page when the caller has at least one of the required permissions", () => {
    mockGetRouteAccess.mockReturnValue({ permissions: ["identity.users.view", "identity.roles.view"] });
    mockAuthState(["identity.roles.view"]);

    renderAt("/app/settings/system/users-roles");

    expect(screen.getByText("Users & roles page")).toBeInTheDocument();
    expect(screen.queryByText(trCommon.noAccessTitle)).toBeNull();
  });

  it("treats a route with no registered access requirement as always visible", () => {
    mockGetRouteAccess.mockReturnValue(undefined);
    mockAuthState([]);

    renderAt("/app/employees");

    expect(screen.getByText("Employees page")).toBeInTheDocument();
  });

  it("blocks a route gated by an access setting the tenant has not enabled", () => {
    mockGetRouteAccess.mockReturnValue({ accessSetting: "showOrgChart" });
    mockAuthState(["employee.orgchart.view"], { accessSettingsBypass: false, access: { showOrgChart: false } });

    renderAt("/app/employees");

    expect(screen.getByText(trCommon.noAccessTitle)).toBeInTheDocument();
  });
});
