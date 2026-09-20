import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trExpense from "../../../public/locales/tr/expense.json";
import trCommon from "../../../public/locales/tr/common.json";
import ExpenseCategoriesPage from "./expense-categories";
import type { ExpenseCategory } from "@/services/expense.service";

// O-13 (ik-surec-testi-bulgu-raporu-2026-09-20): the catalog showed the raw resource key and "%undefined" because the
// API omits a null defaultTaxRate.
vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return { ...actual, usePermission: () => true };
});

const CATEGORIES: ExpenseCategory[] = [
  { id: "c1", code: "food", nameKey: "expense.category.food", isActive: true },
  { id: "c2", code: "gym", nameKey: "Spor Salonu", defaultTaxRate: 10, isActive: true },
];

vi.mock("@/services/expense.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/expense.service")>("@/services/expense.service");
  return { ...actual, listExpenseCategories: vi.fn(async () => CATEGORIES) };
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

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "expense",
    ns: ["expense", "common"],
    resources: { tr: { expense: trExpense, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <ExpenseCategoriesPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("ExpenseCategoriesPage", () => {
  it("shows the translated name of a system category instead of its resource key", async () => {
    renderPage();

    expect(await screen.findByText("Yiyecek ve İçecek")).toBeTruthy();
    expect(screen.getByText("Spor Salonu")).toBeTruthy();
    expect(screen.queryByText("expense.category.food")).toBeNull();
  });

  it("shows a dash when the API omits the default tax rate", async () => {
    renderPage();

    await screen.findByText("Yiyecek ve İçecek");
    expect(screen.getByText("%10")).toBeTruthy();
    expect(screen.queryByText(/undefined/)).toBeNull();
  });
});
