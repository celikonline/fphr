import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { MantineProvider } from "@mantine/core";
import trNavigation from "../../public/locales/tr/navigation.json";
import ScreenSearch from "./screen-search";

// Yetki/modül filtresi bu testin konusu değil: her giriş görünür kabul edilir.
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

function CurrentPath() {
  return <div data-testid="path">{useLocation().pathname}</div>;
}

function renderSearch() {
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
        <MemoryRouter initialEntries={["/app"]}>
          <ScreenSearch />
          <Routes>
            <Route path="*" element={<CurrentPath />} />
          </Routes>
        </MemoryRouter>
      </I18nextProvider>
    </MantineProvider>
  );
}

describe("ScreenSearch", () => {
  it("finds a settings page by its label and shows the group as context", async () => {
    const user = userEvent.setup();
    renderSearch();

    await user.type(screen.getByRole("textbox", { name: "Ekran ara" }), "güvenlik politikası");

    expect(await screen.findByText("Güvenlik politikası")).toBeTruthy();
    expect(screen.getByText(/Ayarlar ›/)).toBeTruthy();
  });

  it("opens the first hit on Enter and clears the query", async () => {
    const user = userEvent.setup();
    renderSearch();
    const input = screen.getByRole("textbox", { name: "Ekran ara" });

    await user.type(input, "güvenlik politikası{Enter}");

    expect(screen.getByTestId("path").textContent).toBe("/app/settings/system/security-policy");
    expect((input as HTMLInputElement).value).toBe("");
  });

  it("says nothing matched for an unknown term", async () => {
    const user = userEvent.setup();
    renderSearch();

    await user.type(screen.getByRole("textbox", { name: "Ekran ara" }), "zzzyyy");

    expect(await screen.findByText("Sonuç bulunamadı")).toBeTruthy();
  });
});
