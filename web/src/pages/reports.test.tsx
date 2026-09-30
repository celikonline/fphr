import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import { MemoryRouter } from "react-router";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trReports from "../../public/locales/tr/reports.json";
import trCommon from "../../public/locales/tr/common.json";
import ReportsPage from "./reports";
import {
  getReportCatalog,
  listReportExecutions,
  requestReport,
  type ReportCatalogItem,
  type ReportExecution,
} from "@/services/reports.service";

vi.mock("@/services/reports.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/reports.service")>(
    "@/services/reports.service"
  );
  return { ...actual, getReportCatalog: vi.fn(), listReportExecutions: vi.fn(), requestReport: vi.fn() };
});

// Everything except the KPI tab is allowed: the page then renders just the catalog + history.
vi.mock("@/components/permission-guard", () => ({
  usePermission: (permission: string) => permission !== "analytics.kpi.view",
  useAnyPermission: () => true,
}));

vi.mock("@/components/employees/use-org-units", () => ({
  useOrgUnits: () => ({ options: [], nameById: new Map(), isLoading: false }),
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

const PAGE_SIZE = 10;
const TOTAL_EXECUTIONS = 25;

const CATALOG: ReportCatalogItem[] = [
  { key: "employee.list", categoryKey: "employee", parameters: [] },
  { key: "leave.requests", categoryKey: "leave", parameters: [] },
  { key: "finance.salaries", categoryKey: "finance", parameters: [] },
];

/** 25 executions "rapor-001..025"; each carries its number in the file name so a page is recognisable in the DOM. */
function executionsFor(page: number) {
  const first = (page - 1) * PAGE_SIZE;
  const items: ReportExecution[] = Array.from(
    { length: Math.min(PAGE_SIZE, TOTAL_EXECUTIONS - first) },
    (_, i) => ({
      id: `exec-${first + i + 1}`,
      reportKey: "leave.requests",
      status: "succeeded",
      requestedAt: "2026-09-20T10:00:00Z",
      resultFileName: `rapor-${String(first + i + 1).padStart(3, "0")}.xlsx`,
    })
  );
  const totalPages = Math.ceil(TOTAL_EXECUTIONS / PAGE_SIZE);
  return {
    items,
    page,
    pageSize: PAGE_SIZE,
    totalCount: TOTAL_EXECUTIONS,
    totalPages,
    hasNext: page < totalPages,
    hasPrevious: page > 1,
  };
}

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "reports",
    ns: ["reports", "common"],
    resources: { tr: { reports: trReports, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter>
            <ReportsPage />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("ReportsPage", () => {
  beforeEach(() => {
    window.localStorage.clear();
    vi.mocked(getReportCatalog).mockResolvedValue(CATALOG);
    vi.mocked(listReportExecutions).mockImplementation(async (params) =>
      executionsFor(params?.page ?? 1)
    );
  });

  it("asks for the next page and shows its rows when the pager moves on", async () => {
    const user = userEvent.setup();
    renderPage();

    expect(await screen.findByText("rapor-001.xlsx")).toBeInTheDocument();
    expect(listReportExecutions).toHaveBeenLastCalledWith({ page: 1, pageSize: PAGE_SIZE });

    await user.click(screen.getByRole("button", { name: "2" }));

    expect(await screen.findByText("rapor-011.xlsx")).toBeInTheDocument();
    expect(listReportExecutions).toHaveBeenLastCalledWith({ page: 2, pageSize: PAGE_SIZE });
    expect(screen.queryByText("rapor-001.xlsx")).not.toBeInTheDocument();
  });

  it("filters the catalog by the search text, ignoring case and Turkish diacritics", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Çalışan listesi raporu");

    await user.type(screen.getByPlaceholderText("Rapor ara"), "IZIN");

    await waitFor(() =>
      expect(screen.queryByText("Çalışan listesi raporu")).not.toBeInTheDocument()
    );
    expect(screen.queryByText("Maaş listesi raporu")).not.toBeInTheDocument();
    // The history table below also names "İzin raporu", so look for the catalog card (a button).
    expect(screen.getByRole("button", { name: /İzin raporu/ })).toBeInTheDocument();
  });

  it("says so when nothing matches the search", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Çalışan listesi raporu");

    await user.type(screen.getByPlaceholderText("Rapor ara"), "zzzz");

    expect(await screen.findByText("Aramanızla eşleşen rapor bulunamadı.")).toBeInTheDocument();
  });

  it("switches to the list view and remembers the choice", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Çalışan listesi raporu");
    expect(screen.queryByRole("columnheader", { name: "Kategori" })).not.toBeInTheDocument();

    await user.click(screen.getByLabelText("Liste görünümü"));

    expect(await screen.findByRole("columnheader", { name: "Kategori" })).toBeInTheDocument();
    expect(window.localStorage.getItem("senseik.reports.catalogView")).toBe("grid");
  });

  it("PDF ve Excel biçim seçicisi format parametresini gönderir", async () => {
    const user = userEvent.setup();
    vi.mocked(requestReport).mockResolvedValue({
      id: "exec-new",
      reportKey: "employee.list",
      status: "queued",
      requestedAt: "2026-09-25T10:00:00Z",
    });
    renderPage();
    await user.click(await screen.findByRole("button", { name: /Çalışan listesi raporu/ }));

    expect(screen.getByRole("radio", { name: "Excel" })).toBeChecked();
    await user.click(screen.getByRole("radio", { name: "PDF" }));
    await user.click(screen.getByRole("button", { name: "Rapor oluştur" }));

    await waitFor(() => expect(requestReport).toHaveBeenCalled());
    expect(vi.mocked(requestReport).mock.calls[0][1]).toMatchObject({ format: "pdf" });
  });

  it("Select parametresini çizer ve seçimi rapor isteğine yansıtır (format ayrıca çizilmez)", async () => {
    const user = userEvent.setup();
    vi.mocked(getReportCatalog).mockResolvedValue([
      {
        key: "attendance.time.daily_status",
        categoryKey: "time",
        parameters: [
          { key: "status", kind: "select", isRequired: true, options: ["late", "absent"] },
          { key: "format", kind: "select", isRequired: false, options: ["xlsx", "pdf"] },
        ],
      },
    ]);
    vi.mocked(requestReport).mockResolvedValue({
      id: "exec-new",
      reportKey: "attendance.time.daily_status",
      status: "queued",
      requestedAt: "2026-09-25T10:00:00Z",
    });
    renderPage();
    await user.click(await screen.findByRole("button", { name: /Günlük durum listeleri/ }));

    expect(screen.getByRole("button", { name: "Rapor oluştur" })).toBeDisabled();
    await user.click(screen.getAllByLabelText("Durum *")[0]);
    await user.click(await screen.findByText("Devamsızlar"));
    await user.click(screen.getByRole("radio", { name: "PDF" }));
    await user.click(screen.getByRole("button", { name: "Rapor oluştur" }));

    await waitFor(() => expect(requestReport).toHaveBeenCalled());
    expect(vi.mocked(requestReport).mock.calls[0][1]).toEqual({ status: "absent", format: "pdf" });
    expect(screen.queryByLabelText(/^Biçim|^format/i)).not.toBeInTheDocument();
  });
});
