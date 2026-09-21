import { beforeAll, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter } from "react-router";
import { MantineProvider } from "@mantine/core";
import trNavigation from "../../../public/locales/tr/navigation.json";
import trSettings from "../../../public/locales/tr/settings.json";
import { SETTINGS_SECTIONS } from "@/config/navigation";
import SettingsHomePage from "./settings-home";

// Yetki/modül filtresi bu testin konusu değil: her bağlantı görünür kabul edilir.
vi.mock("@/hooks/use-nav-visibility", () => ({ useVisibleItems: <T,>(items: readonly T[]) => [...items] }));

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

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "settings",
    ns: ["settings", "navigation"],
    resources: { tr: { settings: trSettings, navigation: trNavigation } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return render(
    <MantineProvider>
      <I18nextProvider i18n={instance}>
        <MemoryRouter>
          <SettingsHomePage />
        </MemoryRouter>
      </I18nextProvider>
    </MantineProvider>
  );
}

describe("SettingsHomePage", () => {
  it("shows every group and link when the search is empty", () => {
    renderPage();
    const total = SETTINGS_SECTIONS.flatMap((section) => section.links).length;
    expect(screen.getAllByRole("heading", { level: 2 })).toHaveLength(SETTINGS_SECTIONS.length);
    expect(screen.getAllByRole("link")).toHaveLength(total);
  });

  it("filters links by label or description, ignoring case and surrounding spaces", () => {
    renderPage();
    fireEvent.change(screen.getByRole("searchbox"), { target: { value: "  DESTEK  " } });
    const links = screen.getAllByRole("link");
    expect(links).toHaveLength(1);
    expect(links[0].getAttribute("href")).toBe("/app/settings/system/support-access");
  });

  it("shows a whole group when the group name matches", () => {
    renderPage();
    fireEvent.change(screen.getByRole("searchbox"), { target: { value: "kullanıcılar ve güvenlik" } });
    const system = SETTINGS_SECTIONS.find((section) => section.key === "system");
    expect(screen.getAllByRole("link")).toHaveLength(system?.links.length ?? -1);
  });

  it("shows an empty state when nothing matches", () => {
    renderPage();
    fireEvent.change(screen.getByRole("searchbox"), { target: { value: "zzzyyy" } });
    expect(screen.queryAllByRole("link")).toHaveLength(0);
    expect(screen.getByRole("status").textContent).toContain("zzzyyy");
  });
});
