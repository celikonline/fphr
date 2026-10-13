import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trPayroll from "../../../public/locales/tr/payroll.json";
import { PeriodPreview } from "./period-preview";

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return { ...actual, usePermission: () => true };
});

vi.mock("@/services/payroll.service", () => ({
  previewPayrollPeriod: vi.fn(async () => ({
    calculatedCount: 3,
    skippedCount: 3,
    totalGross: 1000,
    totalNet: 800,
    totalEmployerCost: 1200,
    lines: [],
    skipped: [
      { employeeId: "e1", reason: "pay_item_formula_failed" },
      { employeeId: "e2", reason: "pay_item_formula_failed" },
      { employeeId: "e3", reason: "wage_basis_mismatch" },
    ],
    warnings: [{ employeeId: "e4", code: "pay_item_negative_result" }],
  })),
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

describe("PeriodPreview", () => {
  it("lists skip reasons and warnings with translated labels and counts", async () => {
    const instance = i18next.createInstance();
    void instance.init({
      lng: "tr",
      fallbackLng: "tr",
      defaultNS: "payroll",
      ns: ["payroll"],
      resources: { tr: { payroll: trPayroll } },
      interpolation: { escapeValue: false },
      react: { useSuspense: false },
    });
    const user = userEvent.setup();
    render(
      <I18nextProvider i18n={instance}>
        <QueryClientProvider client={new QueryClient()}>
          <MantineProvider>
            <PeriodPreview periodId="p1" status="open" />
          </MantineProvider>
        </QueryClientProvider>
      </I18nextProvider>,
    );

    await user.click(screen.getByRole("button", { name: "Deneme hesapla" }));

    const skips = await screen.findByTestId("preview-skip-reasons");
    expect(within(skips).getByText(/Bir ödeme türünün formülü hesaplanamadı.*\(2 çalışan\)/)).toBeInTheDocument();
    expect(within(skips).getByText(/ücret esası ile maaş kaydının ödeme düzeni uyuşmuyor \(1 çalışan\)/)).toBeInTheDocument();
    expect(within(screen.getByTestId("preview-warnings")).getByText(/negatif çıktı, 0 olarak alındı \(1 çalışan\)/)).toBeInTheDocument();
  });
});
