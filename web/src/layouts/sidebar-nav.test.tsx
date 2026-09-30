import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { Home } from "lucide-react";
import { MemoryRouter } from "react-router";
import trNavigation from "../../public/locales/tr/navigation.json";
import { NavItemLink, NavList } from "./sidebar-nav";
import { useAuthStore } from "@/store/auth.store";
import type { NavItem } from "@/config/navigation";

// senseik-takim: 11.10 gruplu, açılır-kapanır, aramalı sidebar - grup açma/kapama, aktif rotanın grubunu
// otomatik açması, arama filtresi, izinsiz grupların gizlenmesi, daraltılmış moddaki grup menüsü ve "Yakında"
// (phase) rozeti/disabled davranışı.

vi.mock("@/store/auth.store", () => ({
  useAuthStore: vi.fn(),
}));

const mockUseAuthStore = useAuthStore as unknown as ReturnType<typeof vi.fn>;

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
  Object.defineProperty(document, "fonts", {
    writable: true,
    value: { addEventListener: () => {}, removeEventListener: () => {} },
  });
});

function setAuthState(permissions: string[]) {
  const state = {
    user: {
      id: "u1",
      firstName: "Ada",
      lastName: "Lovelace",
      email: "ada@acme.test",
      name: "Ada Lovelace",
      permissions,
      tenant: { id: "t1", name: "Acme Anonim Şirketi", slug: "acme" },
    },
    features: { accessSettingsBypass: true, access: {} },
    hasPermission: (permission: string) => permissions.includes(permission),
    logout: vi.fn(),
  };
  mockUseAuthStore.mockImplementation((selector: (s: typeof state) => unknown) => selector(state));
}

beforeEach(() => {
  vi.clearAllMocks();
  setAuthState([]);
});

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "navigation",
    ns: ["navigation"],
    resources: { tr: { navigation: trNavigation } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderNavList(collapsed: boolean, initialEntries: string[] = ["/app"]) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter initialEntries={initialEntries}>
            <NavList collapsed={collapsed} />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("grouped sidebar - expand/collapse", () => {
  it("opens the group containing the active route by default", () => {
    setAuthState(["employee.employees.read"]);
    renderNavList(false, ["/app/employees"]);

    const groupButton = screen.getByRole("button", { name: "İnsan Kaynakları" });
    expect(groupButton).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("link", { name: "Çalışanlar" })).toBeDefined();
  });

  it("stays closed by default off its route, and toggles open/closed on click", async () => {
    const user = userEvent.setup();
    setAuthState(["employee.employees.read"]);
    renderNavList(false, ["/app"]);

    const groupButton = screen.getByRole("button", { name: "İnsan Kaynakları" });
    expect(groupButton).toHaveAttribute("aria-expanded", "false");

    await user.click(groupButton);
    expect(groupButton).toHaveAttribute("aria-expanded", "true");

    await user.click(groupButton);
    expect(groupButton).toHaveAttribute("aria-expanded", "false");
  });
});

describe("grouped sidebar - permission visibility", () => {
  it("hides a group entirely when the user has none of its items' permissions", () => {
    setAuthState([]);
    renderNavList(false, ["/app"]);

    expect(screen.queryByRole("button", { name: "İnsan Kaynakları" })).toBeNull();
    expect(screen.queryByRole("button", { name: "İşe Alım" })).toBeNull();
  });
});

describe("grouped sidebar - search", () => {
  it("filters to the matching group (by group label) and force-opens it", async () => {
    const user = userEvent.setup();
    setAuthState(["employee.employees.read"]);
    renderNavList(false, ["/app"]);

    await user.type(screen.getByPlaceholderText("Menüde ara…"), "İnsan Kaynak");

    const groupButton = screen.getByRole("button", { name: "İnsan Kaynakları" });
    expect(groupButton).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("link", { name: "Çalışanlar" })).toBeDefined();
    // A group with no visible match ("Talep" loose item is unrelated) drops out of the results.
    expect(screen.queryByText("Talep")).toBeNull();
  });

  it("shows a no-results message when nothing matches", async () => {
    const user = userEvent.setup();
    setAuthState(["employee.employees.read"]);
    renderNavList(false, ["/app"]);

    await user.type(screen.getByPlaceholderText("Menüde ara…"), "zzz-yok-boyle-bir-sey");

    expect(screen.getByRole("status")).toHaveTextContent("Sonuç bulunamadı");
  });
});

describe("grouped sidebar - collapsed mode", () => {
  it("shows a group as a single icon that opens a side menu with its sub-items", async () => {
    const user = userEvent.setup();
    setAuthState(["employee.employees.read"]);
    renderNavList(true, ["/app"]);

    const groupTrigger = screen.getByRole("button", { name: "İnsan Kaynakları" });
    await user.click(groupTrigger);

    expect(await screen.findByRole("menuitem", { name: "Çalışanlar" })).toBeDefined();
  });

  it("marks the group of the current page as current, so it carries the rail's özlük sekmesi", () => {
    setAuthState(["employee.employees.read"]);
    renderNavList(true, ["/app/employees"]);

    expect(screen.getByRole("button", { name: "İnsan Kaynakları" })).toHaveAttribute(
      "data-current"
    );
  });

  it("leaves groups unmarked when the current page is outside them", () => {
    setAuthState(["employee.employees.read"]);
    renderNavList(true, ["/app"]);

    expect(screen.getByRole("button", { name: "İnsan Kaynakları" })).not.toHaveAttribute(
      "data-current"
    );
  });
});

describe("NavItemLink - planned (phase) items", () => {
  it("renders as disabled with a Yakında badge instead of a navigable link", () => {
    const plannedItem: NavItem = {
      key: "future-feature",
      labelKey: "nav.home",
      path: "/app/future-feature",
      icon: Home,
      phase: 4,
    };

    render(
      <I18nextProvider i18n={createTestI18n()}>
        <MantineProvider>
          <MemoryRouter>
            <NavItemLink item={plannedItem} collapsed={false} label="Gelecek özellik" />
          </MemoryRouter>
        </MantineProvider>
      </I18nextProvider>
    );

    expect(screen.getByText("Gelecek özellik")).toBeDefined();
    expect(screen.getByText("Yakında")).toBeDefined();
    expect(screen.queryByRole("link", { name: /Gelecek özellik/ })).toBeNull();
  });
});
