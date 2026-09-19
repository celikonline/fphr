import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MantineProvider } from "@mantine/core";
import { MemoryRouter, Routes, Route } from "react-router";
import trNavigation from "../../public/locales/tr/navigation.json";
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

  // Only items without a `permissions` list (e.g. "home") are relied on in these tests, so the
  // exact permission strings here don't matter - they just need to exist for state.hasPermission.
  const state = {
    user: { id: "u1", firstName: "Ada", name: "Ada Lovelace", permissions: [] as string[] },
    features: { accessSettingsBypass: true, access: {} },
    hasPermission: (permission: string) => state.user.permissions.includes(permission),
    logout: vi.fn(),
  };
  mockUseAuthStore.mockImplementation((selector: (s: typeof state) => unknown) => selector(state));
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

function renderAppLayout() {
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <MantineProvider>
        <MemoryRouter initialEntries={["/app"]}>
          <Routes>
            <Route path="/app" element={<AppLayout />}>
              <Route index element={<div>Page content</div>} />
            </Route>
          </Routes>
        </MemoryRouter>
      </MantineProvider>
    </I18nextProvider>,
  );
}

describe("AppLayout sidebar collapse toggle", () => {
  it("expands by default and shows nav labels in the desktop sidebar", () => {
    renderAppLayout();
    const desktopSidebar = screen.getByTestId("sidebar-desktop");

    expect(desktopSidebar.className).toContain("w-52");
    expect(within(desktopSidebar).getByText("Ana sayfa")).toBeDefined();
  });

  it("collapses the desktop sidebar to icon-only width when the toggle is clicked", async () => {
    const user = userEvent.setup();
    renderAppLayout();
    const desktopSidebar = screen.getByTestId("sidebar-desktop");

    await user.click(screen.getByRole("button", { name: "Menüyü daralt" }));

    expect(desktopSidebar.className).toContain("w-14");
    expect(desktopSidebar.className).not.toContain("w-52");
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

    expect(desktopSidebar.className).toContain("w-52");
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
