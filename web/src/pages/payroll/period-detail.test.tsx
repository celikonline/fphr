import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { MemoryRouter, Route, Routes } from "react-router";
import trPayroll from "../../../public/locales/tr/payroll.json";
import trCommon from "../../../public/locales/tr/common.json";
import PayrollPeriodDetailPage from "./period-detail";
import { getPayrollPeriod, runPayrollPeriod, type PayrollPeriodStatus } from "@/services/payroll.service";

const toastMock = vi.fn();
vi.mock("@/hooks/use-toast", () => ({ toast: (...args: unknown[]) => toastMock(...args) }));

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return { ...actual, usePermission: () => true };
});

vi.mock("@/services/organization.service", () => ({ getCostCenters: vi.fn(async () => []) }));

// Sibling panels run their own queries; they are irrelevant to the empty state under test.
vi.mock("./closing-checklist", () => ({ ClosingChecklist: () => null }));
vi.mock("./period-actions", () => ({ PeriodActions: () => null }));
vi.mock("./period-adjustments", () => ({ PeriodAdjustments: () => null }));
vi.mock("./period-comparison", () => ({ PeriodComparison: () => null }));
vi.mock("./period-preview", () => ({ PeriodPreview: () => null }));
vi.mock("./period-receipt-matching", () => ({ PeriodReceiptMatching: () => null }));

vi.mock("@/services/payroll.service", () => ({
  getPayrollPeriod: vi.fn(),
  runPayrollPeriod: vi.fn(async () => ({ runId: "r1", calculatedCount: 4, skippedCount: 2, skipped: [], warnings: [] })),
  downloadPayrollBankFile: vi.fn(),
  downloadPayrollDeclarationSummary: vi.fn(),
  downloadPayrollPeriodExport: vi.fn(),
  saveDownloadedPayrollFile: vi.fn(),
}));

const GUIDANCE = /Bordro satırları "Hesapla" ile üretilir/;

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
  toastMock.mockClear();
  vi.mocked(runPayrollPeriod).mockClear();
});

function mockPeriod(status: PayrollPeriodStatus) {
  vi.mocked(getPayrollPeriod).mockResolvedValue({ id: "p1", year: 2026, month: 8, status, payslips: [] });
}

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "payroll",
    ns: ["payroll", "common"],
    resources: { tr: { payroll: trPayroll, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter initialEntries={["/app/payroll/periods/p1"]}>
            <Routes>
              <Route path="/app/payroll/periods/:id" element={<PayrollPeriodDetailPage />} />
            </Routes>
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("PayrollPeriodDetailPage empty state", () => {
  it("runs the calculation from the empty state of an open period and reports the counts", async () => {
    mockPeriod("open");
    renderPage();
    const user = userEvent.setup();

    expect(await screen.findByText(GUIDANCE)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Çalışanlar/ })).toHaveAttribute("href", "/app/employees");
    await user.click(screen.getByRole("button", { name: "Hesapla" }));

    await waitFor(() => expect(runPayrollPeriod).toHaveBeenCalledWith("p1"));
    await waitFor(() =>
      expect(toastMock).toHaveBeenCalledWith(
        expect.objectContaining({ description: "4 çalışan için bordro hesaplandı, 2 çalışan atlandı." }),
      ),
    );
  });

  it("does not offer the calculation when the period is not open", async () => {
    mockPeriod("approved");
    renderPage();

    expect(await screen.findByText(GUIDANCE)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Hesapla" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Çalışanlar/ })).toBeInTheDocument();
  });
});
