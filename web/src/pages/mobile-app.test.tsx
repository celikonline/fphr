import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MantineProvider } from "@mantine/core";
import trMobileApp from "../../public/locales/tr/mobileApp.json";
import { storeUrl } from "@/components/mobile-app/store-url";
import MobileAppPage from "./mobile-app";

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

afterEach(() => {
  vi.unstubAllEnvs();
});

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "mobileApp",
    ns: ["mobileApp"],
    resources: { tr: { mobileApp: trMobileApp } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return render(
    <MantineProvider>
      <I18nextProvider i18n={instance}>
        <MobileAppPage />
      </I18nextProvider>
    </MantineProvider>
  );
}

describe("storeUrl", () => {
  it("returns null for missing or blank values", () => {
    expect(storeUrl(undefined)).toBeNull();
    expect(storeUrl("")).toBeNull();
    expect(storeUrl("   ")).toBeNull();
  });

  it("returns null for a non-https url", () => {
    expect(storeUrl("http://apps.apple.com/app/senseik")).toBeNull();
  });

  it("returns the trimmed url for a valid https url", () => {
    expect(storeUrl("https://apps.apple.com/app/senseik")).toBe(
      "https://apps.apple.com/app/senseik"
    );
    expect(storeUrl("  https://play.google.com/store/apps/senseik  ")).toBe(
      "https://play.google.com/store/apps/senseik"
    );
  });
});

describe("MobileAppPage", () => {
  it("disables both store buttons, showing 'Yakında' + the store name (not 'İndir') when no store urls are configured", () => {
    vi.stubEnv("VITE_IOS_APP_URL", "");
    vi.stubEnv("VITE_ANDROID_APP_URL", "");
    renderPage();

    const iosButton = screen.getByRole("button", { name: /yakında.*app store/i });
    const androidButton = screen.getByRole("button", { name: /yakında.*google play/i });
    expect(iosButton).toBeDisabled();
    expect(androidButton).toBeDisabled();
    expect(screen.getAllByText(/^yakında$/i)).toHaveLength(2);
    expect(screen.queryByText(/^İndir$/)).toBeNull();
  });

  it("enables a store button as a link with 'İndir' when its url is configured", () => {
    vi.stubEnv("VITE_IOS_APP_URL", "https://apps.apple.com/app/senseik");
    vi.stubEnv("VITE_ANDROID_APP_URL", "");
    renderPage();

    const iosLink = screen.getByRole("link", { name: /app store.*İndir/i });
    expect(iosLink).toHaveAttribute("href", "https://apps.apple.com/app/senseik");
    expect(iosLink).toHaveAttribute("target", "_blank");
    expect(iosLink).toHaveAttribute("rel", "noopener noreferrer");
    // Android stays disabled: still "Yakında" + store name, not "İndir".
    expect(screen.getByRole("button", { name: /yakında.*google play/i })).toBeDisabled();
    expect(screen.getAllByText(/^İndir$/)).toHaveLength(1);
  });

  it("switches the showcase screenshot and aria-pressed state when a feature is selected", () => {
    renderPage();

    const approvalsButton = screen.getByRole("button", { name: /onaylar/i });
    expect(approvalsButton).toHaveAttribute("aria-pressed", "false");

    fireEvent.click(approvalsButton);

    expect(approvalsButton).toHaveAttribute("aria-pressed", "true");
    const showcaseImage = screen.getByAltText(trMobileApp.features.approvals.alt);
    expect(showcaseImage).toHaveAttribute("src", "/images/mobile-app/approvals.jpg");
  });
});
