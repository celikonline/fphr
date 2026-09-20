import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trHome from "../../public/locales/tr/home.json";
import trNavigation from "../../public/locales/tr/navigation.json";
import trCommon from "../../public/locales/tr/common.json";
import { useAuthStore } from "@/store/auth.store";
import HomePage from "./home";

// senseik-042 (halka grafik merkezi): the donut's center must show the grand total headcount,
// never a department name - the previous behavior showed the largest department's name/count.
vi.mock("@/services/employees.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/employees.service")>("@/services/employees.service");
  return {
    ...actual,
    getDepartmentDistribution: vi.fn().mockResolvedValue([
      { orgUnitId: "1", departmentName: "Mühendislik", count: 7 },
      { orgUnitId: "2", departmentName: "Satış", count: 3 },
    ]),
  };
});

// jsdom has neither matchMedia nor ResizeObserver; Mantine/recharts need both to render without throwing.
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
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "home",
    ns: ["home", "navigation", "common"],
    resources: { tr: { home: trHome, navigation: trNavigation, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

/** Only `employee.directory.view` is granted, and no access-setting feature is enabled, so of every
 * gated widget on the dashboard only "Çalışan Dağılımı" (department distribution) mounts - keeping
 * this test from having to mock every other home widget's service call. */
function signInWithDirectoryPermissionOnly() {
  useAuthStore.setState({
    hasHydrated: true,
    token: "test-token",
    user: {
      id: "u1",
      email: "user@example.com",
      name: "Test User",
      roles: [],
      roleLabels: [],
      permissions: ["employee.directory.view"],
    },
    features: { access: {} as never, accessSettingsBypass: false, mfaSetupRequired: false, disabledModules: [] },
  });
}

function renderHome() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter initialEntries={["/app"]}>
            <HomePage />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("HomePage department distribution donut (senseik-042)", () => {
  it("shows the total headcount at the donut's center, not the department name", async () => {
    signInWithDirectoryPermissionOnly();
    renderHome();

    const widget = await screen.findByText("Çalışan Dağılımı");
    const card = widget.closest(".mantine-Card-root") as HTMLElement;
    expect(card).not.toBeNull();

    // Total (7 + 3 = 10) must appear in the chart's center label, labeled "Toplam".
    const centerTotal = await screen.findByTestId("department-distribution-center-total");
    await waitFor(() => expect(centerTotal).toHaveTextContent("10"));
    expect(within(card).getByText("Toplam")).toBeInTheDocument();

    // Neither department name is shown as the center label (the pre-fix behavior).
    expect(within(card).queryByText("Mühendislik")).toBeNull();
    expect(within(card).queryByText("Satış")).toBeNull();
  });
});
