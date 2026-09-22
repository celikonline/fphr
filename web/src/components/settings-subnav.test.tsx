import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter } from "react-router";
import { MantineProvider } from "@mantine/core";
import trNavigation from "../../public/locales/tr/navigation.json";
import { SETTINGS_SECTIONS } from "@/config/navigation";
import SettingsSubNav, { isSettingsSubPage } from "./settings-subnav";

// Yetki/modül filtresi bu testin konusu değil: her bağlantı görünür kabul edilir.
vi.mock("@/hooks/use-nav-visibility", () => ({
  useVisibleItems: <T,>(items: readonly T[]) => [...items],
}));

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

function renderAt(path: string) {
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
  return render(
    <MantineProvider>
      <I18nextProvider i18n={instance}>
        <MemoryRouter initialEntries={[path]}>
          <SettingsSubNav />
        </MemoryRouter>
      </I18nextProvider>
    </MantineProvider>
  );
}

describe("isSettingsSubPage", () => {
  it("is true only below the settings hub", () => {
    expect(isSettingsSubPage("/app/settings/system/users-roles")).toBe(true);
    expect(isSettingsSubPage("/app/settings/data-transfer/jobs/abc")).toBe(true);
    expect(isSettingsSubPage("/app/settings")).toBe(false);
    expect(isSettingsSubPage("/app/settings/")).toBe(false);
    expect(isSettingsSubPage("/app/settingsx/foo")).toBe(false);
    expect(isSettingsSubPage("/app/employees")).toBe(false);
  });
});

describe("SettingsSubNav", () => {
  it("renders nothing on the settings hub", () => {
    renderAt("/app/settings");
    expect(screen.queryByTestId("settings-subnav")).toBeNull();
  });

  it("lists every settings link plus the hub link on a settings sub page", () => {
    renderAt("/app/settings/company/profile");
    const nav = screen.getByTestId("settings-subnav");
    const total = SETTINGS_SECTIONS.flatMap((section) => section.links).length;
    expect(within(nav).getAllByRole("link")).toHaveLength(total + 1);
  });

  it("marks the current page as active", () => {
    renderAt("/app/settings/system/users-roles");
    const nav = screen.getByTestId("settings-subnav");
    const active = within(nav)
      .getAllByRole("link")
      .filter((link) => link.getAttribute("aria-current") === "page");
    expect(active).toHaveLength(1);
    expect(active[0].getAttribute("href")).toBe("/app/settings/system/users-roles");
  });

  it("keeps the parent link active on nested pages", () => {
    renderAt("/app/settings/data-transfer/jobs/abc");
    const nav = screen.getByTestId("settings-subnav");
    const active = within(nav)
      .getAllByRole("link")
      .find((link) => link.getAttribute("aria-current") === "page");
    expect(active?.getAttribute("href")).toBe("/app/settings/data-transfer");
  });
});
