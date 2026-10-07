import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MantineProvider } from "@mantine/core";
import { MemoryRouter, Routes, Route, useLocation } from "react-router";
import trNavigation from "../../public/locales/tr/navigation.json";
import trCommon from "../../public/locales/tr/common.json";
import AppLayout from "./app-layout";
import { useAuthStore } from "@/store/auth.store";

// senseik-020: the sidebar's collapsed state used to be fixed by the responsive breakpoint only
// (desktop always expanded, mobile always icon-only rail). These tests cover the user-triggered
// desktop toggle added in app-layout.tsx, and guard the pre-existing mobile rail behavior.

vi.mock("@/store/auth.store", () => ({
  useAuthStore: vi.fn(),
}));

// Real useImpersonatorId reads the JWT via getImpersonatorId(state.token); stub it directly so the
// banner stays hidden without needing a real token in the mocked auth store.
vi.mock("@/hooks/use-access-setting", () => ({
  useImpersonatorId: vi.fn(() => null),
}));

const mockUseAuthStore = useAuthStore as unknown as ReturnType<typeof vi.fn>;
const mockLogout = vi.fn();

// jsdom has no matchMedia/ResizeObserver/document.fonts; Mantine's Tooltip/Menu positioning
// (Floating UI) needs one of these mounted to render without throwing.
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

beforeEach(() => {
  localStorage.clear();
  vi.clearAllMocks();

  setViewer();
});

function setViewer(permissions: string[] = [], disabledModules: string[] = []) {
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
    features: { accessSettingsBypass: true, access: {}, disabledModules },
    hasPermission: (permission: string) => state.user.permissions.includes(permission),
    logout: mockLogout,
  };
  mockUseAuthStore.mockImplementation((selector: (s: typeof state) => unknown) => selector(state));
}

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "navigation",
    ns: ["navigation", "common"],
    resources: { tr: { navigation: trNavigation, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderAppLayout(initialEntries: string[] = ["/app"], initialIndex?: number) {
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <MantineProvider>
        <MemoryRouter initialEntries={initialEntries} initialIndex={initialIndex}>
          <CurrentPath />
          <Routes>
            <Route path="/app" element={<AppLayout />}>
              <Route index element={<div>Page content</div>} />
              <Route path="reports" element={<div>Reports page</div>} />
              <Route path="*" element={<div>Request page</div>} />
            </Route>
          </Routes>
        </MemoryRouter>
      </MantineProvider>
    </I18nextProvider>
  );
}

function CurrentPath() {
  const location = useLocation();
  return <div data-testid="current-path">{location.pathname}{location.search}</div>;
}

describe("AppLayout tenant badge", () => {
  it("shows the company inside the profile menu when it is opened", async () => {
    const user = userEvent.setup();
    renderAppLayout();

    expect(screen.queryByTestId("tenant-badge")).toBeNull();
    await user.click(screen.getByRole("button", { name: "Profilim" }));
    expect(
      within(await screen.findByTestId("tenant-badge")).getByText("Acme Anonim Şirketi")
    ).toBeDefined();
  });
});

describe("AppLayout create shortcut", () => {
  it.each([
    ["leave.my.requests", "/app/leave"],
    ["leave.my.overtimes", "/app/leave/overtimes"],
    ["expense.my.claims", "/app/payments"],
    ["expense.my.advances", "/app/payments"],
  ])("opens the request hub from an existing page with %s", async (permission, path) => {
    setViewer([permission]);
    const user = userEvent.setup();
    renderAppLayout([path]);

    const shortcut = screen.getByRole("link", { name: "Oluştur" });
    expect(shortcut).toHaveAttribute("href", "/app/requests");
    await user.click(shortcut);

    expect(screen.getByTestId("current-path")).toHaveTextContent(/^\/app\/requests$/);
  });

  it("hides the shortcut for unrelated permissions", () => {
    setViewer(["employee.employees.read"]);
    renderAppLayout();

    expect(screen.queryByRole("link", { name: "Oluştur" })).toBeNull();
  });

  it("keeps the hub shortcut when an allowed module is still enabled", async () => {
    setViewer(["leave.my.requests", "leave.my.overtimes", "expense.my.claims"], ["leave"]);
    const user = userEvent.setup();
    renderAppLayout();

    await user.click(screen.getByRole("link", { name: "Oluştur" }));
    expect(screen.getByTestId("current-path")).toHaveTextContent(/^\/app\/requests$/);
  });

  it("hides the create control when no requests are available", () => {
    renderAppLayout();
    expect(screen.queryByRole("link", { name: "Oluştur" })).toBeNull();
  });

  it("hides the create control when all permitted modules are disabled", () => {
    setViewer(["leave.my.requests", "expense.my.advances"], ["leave", "expense"]);
    renderAppLayout();
    expect(screen.queryByRole("link", { name: "Oluştur" })).toBeNull();
  });
});

describe("AppLayout settings shortcut", () => {
  it("only shows the shortcut with a settings permission", () => {
    renderAppLayout();
    expect(within(screen.getByRole("banner")).queryByRole("link", { name: "Ayarlar" })).toBeNull();
  });

  it("links to the settings hub with the same permission gate as navigation", () => {
    setViewer(["identity.tenant.view"]);
    renderAppLayout();
    expect(within(screen.getByRole("banner")).getByRole("link", { name: "Ayarlar" })).toHaveAttribute("href", "/app/settings");
  });
});

describe("AppLayout search shortcut", () => {
  it("focuses the screen search with Ctrl+K", async () => {
    const user = userEvent.setup();
    renderAppLayout();

    await user.keyboard("{Control>}k{/Control}");

    expect(screen.getByRole("textbox", { name: "Ekran ara" })).toHaveFocus();
  });
});

describe("AppLayout user menu", () => {
  it("opens from the top bar with the profile link and sign out", async () => {
    const user = userEvent.setup();
    renderAppLayout();

    await user.click(screen.getByRole("button", { name: "Profilim" }));

    expect(await screen.findByText("ada@acme.test")).toBeDefined();
    expect(screen.getByText("Profilim").closest("a")?.getAttribute("href")).toBe("/app/account");

    await user.click(screen.getByText("Çıkış Yap"));

    expect(mockLogout).toHaveBeenCalledTimes(1);
  });

  it("no longer lists the profile in the sidebar", () => {
    renderAppLayout();

    expect(within(screen.getByTestId("sidebar-desktop")).queryByText("Profilim")).toBeNull();
  });
});

describe("AppLayout back button", () => {
  it("is hidden on the home page", () => {
    renderAppLayout();

    expect(screen.queryByRole("button", { name: "Geri" })).toBeNull();
  });

  it("goes back in history on inner pages", async () => {
    const user = userEvent.setup();
    renderAppLayout(["/app", "/app/reports"], 1);

    // The button is only shown off the home page, so its disappearance proves we landed on /app.
    await user.click(screen.getByRole("button", { name: "Geri" }));

    expect(screen.queryByRole("button", { name: "Geri" })).toBeNull();
  });

  it("goes home when the page was opened directly", async () => {
    const user = userEvent.setup();
    renderAppLayout(["/app/reports"]);

    await user.click(screen.getByRole("button", { name: "Geri" }));

    expect(screen.queryByRole("button", { name: "Geri" })).toBeNull();
  });
});

describe("AppLayout sidebar collapse toggle", () => {
  it("expands by default and shows nav labels in the desktop sidebar", () => {
    renderAppLayout();
    const desktopSidebar = screen.getByTestId("sidebar-desktop");

    expect(desktopSidebar.className).toContain("w-56");
    expect(within(desktopSidebar).getByText("Ana sayfa")).toBeDefined();
  });

  it("collapses the desktop sidebar to icon-only width when the toggle is clicked", async () => {
    const user = userEvent.setup();
    renderAppLayout();
    const desktopSidebar = screen.getByTestId("sidebar-desktop");

    await user.click(screen.getByRole("button", { name: "Menüyü daralt" }));

    expect(desktopSidebar.className).toContain("w-14");
    expect(desktopSidebar.className).not.toContain("w-64");
    expect(within(desktopSidebar).queryByText("Ana sayfa")).toBeNull();
    // Collapsed nav items stay reachable via their tooltip/aria-label instead of visible text.
    expect(within(desktopSidebar).getByLabelText("Ana sayfa")).toBeDefined();
  });

  it("toggles back to expanded on a second click", async () => {
    const user = userEvent.setup();
    renderAppLayout();
    const desktopSidebar = screen.getByTestId("sidebar-desktop");

    await user.click(screen.getByRole("button", { name: "Menüyü daralt" }));
    await user.click(screen.getByRole("button", { name: "Menüyü genişlet" }));

    expect(desktopSidebar.className).toContain("w-56");
    expect(within(desktopSidebar).getByText("Ana sayfa")).toBeDefined();
  });

  it("remembers the collapsed preference across remounts (localStorage)", async () => {
    const user = userEvent.setup();
    const first = renderAppLayout();
    await user.click(screen.getByRole("button", { name: "Menüyü daralt" }));
    first.unmount();

    renderAppLayout();
    const desktopSidebar = screen.getByTestId("sidebar-desktop");

    expect(desktopSidebar.className).toContain("w-14");
  });

  it("regression: the mobile icon rail stays icon-only regardless of the desktop toggle", async () => {
    const user = userEvent.setup();
    renderAppLayout();
    const mobileSidebar = screen.getByTestId("sidebar-mobile");

    expect(mobileSidebar.className).toContain("w-14");
    expect(within(mobileSidebar).queryByText("Ana sayfa")).toBeNull();

    await user.click(screen.getByRole("button", { name: "Menüyü daralt" }));

    // Toggling the desktop preference must not change the mobile rail's markup at all.
    expect(mobileSidebar.className).toContain("w-14");
    expect(within(mobileSidebar).queryByText("Ana sayfa")).toBeNull();
  });
});
