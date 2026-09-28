import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trEmployees from "../../../public/locales/tr/employees.json";
import trCommon from "../../../public/locales/tr/common.json";
import { cardPdfPeriod, EmployeeCardPdfDialog } from "./employee-card-pdf-dialog";
import { useAuthStore } from "@/store/auth.store";

const today = new Date(2026, 8, 28); // 28.09.2026 (months are 0-based)

describe("cardPdfPeriod", () => {
  it("last12Months spans one year back to today", () => {
    expect(cardPdfPeriod("last12Months", today, { from: "", to: "" })).toEqual({ from: "2025-09-28", to: "2026-09-28" });
  });

  it("thisYear starts on 1 January", () => {
    expect(cardPdfPeriod("thisYear", today, { from: "", to: "" })).toEqual({ from: "2026-01-01", to: "2026-09-28" });
  });

  it("allTime sends no bounds", () => {
    expect(cardPdfPeriod("allTime", today, { from: "", to: "" })).toEqual({});
  });

  it("custom with from after to is invalid", () => {
    expect(cardPdfPeriod("custom", today, { from: "2026-05-01", to: "2026-01-01" })).toBeNull();
  });

  it("custom with an empty bound is invalid", () => {
    expect(cardPdfPeriod("custom", today, { from: "2026-01-01", to: "" })).toBeNull();
  });

  it("custom passes both bounds through", () => {
    expect(cardPdfPeriod("custom", today, { from: "2026-01-01", to: "2026-03-31" })).toEqual({
      from: "2026-01-01",
      to: "2026-03-31",
    });
  });
});

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

beforeEach(() => {
  useAuthStore.setState({
    hasPermission: () => true,
    isAccessSettingEnabled: () => true,
    isModuleEnabled: () => true,
  });
});

afterEach(() => {
  vi.clearAllMocks();
});

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "employees",
    ns: ["employees", "common"],
    resources: { tr: { employees: trEmployees, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderDialog() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <EmployeeCardPdfDialog employeeId="emp-1" isOwnCard={false} onClose={vi.fn()} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("EmployeeCardPdfDialog", () => {
  it("does not offer the 'other' tab (shortcuts only, nothing to print)", () => {
    renderDialog();
    expect(screen.queryByRole("checkbox", { name: trEmployees.card.tabs.other })).not.toBeInTheDocument();
  });

  it("disables the generate button once every section is cleared", async () => {
    const user = userEvent.setup();
    renderDialog();

    const generate = screen.getByRole("button", { name: trEmployees.card.pdf.generate });
    expect(generate).not.toBeDisabled();

    await user.click(screen.getByRole("button", { name: trEmployees.card.pdf.clearAll }));

    expect(generate).toBeDisabled();
  });
});
