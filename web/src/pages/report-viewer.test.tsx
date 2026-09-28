import { StrictMode } from "react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError, type AxiosResponse } from "axios";
import { I18nextProvider } from "react-i18next";
import { MemoryRouter, Route, Routes } from "react-router";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trReports from "../../public/locales/tr/reports.json";
import trCommon from "../../public/locales/tr/common.json";
import ReportViewerPage from "./report-viewer";
import {
  downloadReportExecution,
  getReportCatalog,
  getReportExecution,
  getReportExecutionData,
  requestReport,
  saveDownloadedReportFile,
  type ReportCatalogItem,
  type ReportData,
  type ReportExecution,
} from "@/services/reports.service";

vi.mock("@/services/reports.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/reports.service")>("@/services/reports.service");
  return {
    ...actual,
    getReportCatalog: vi.fn(),
    getReportExecution: vi.fn(),
    getReportExecutionData: vi.fn(),
    requestReport: vi.fn(),
    downloadReportExecution: vi.fn(),
    saveDownloadedReportFile: vi.fn(),
  };
});

vi.mock("@/components/permission-guard", () => ({
  usePermission: () => true,
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

const NO_FILTER: ReportCatalogItem = { key: "employee.list", categoryKey: "employee", parameters: [] };
const WITH_YEAR: ReportCatalogItem = {
  key: "employee.departed",
  categoryKey: "employee",
  parameters: [{ key: "year", kind: "year", isRequired: true }],
};

const EXECUTION: ReportExecution = {
  id: "exec-1",
  reportKey: "employee.list",
  status: "succeeded",
  requestedAt: "2026-09-28T10:00:00Z",
  resultFileName: "Çalışan listesi.xlsx",
};

const DATA: ReportData = {
  sheets: [{ index: 0, name: "Çalışanlar" }],
  sheetIndex: 0,
  columns: [{ header: "Ad", type: "text" }],
  rows: { items: [["Ayşe"], ["Mehmet"]], page: 1, pageSize: 50, totalCount: 2, totalPages: 1, hasNext: false, hasPrevious: false },
};

function renderPage(entry: string, { strict = false } = {}) {
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
  const tree = (
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter initialEntries={[entry]}>
            <Routes>
              <Route path="/app/reports/:key" element={<ReportViewerPage />} />
            </Routes>
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
  return render(strict ? <StrictMode>{tree}</StrictMode> : tree);
}

describe("ReportViewerPage", () => {
  beforeEach(() => {
    vi.mocked(getReportCatalog).mockResolvedValue([NO_FILTER, WITH_YEAR]);
    vi.mocked(requestReport).mockResolvedValue(EXECUTION);
    vi.mocked(getReportExecution).mockResolvedValue(EXECUTION);
    vi.mocked(getReportExecutionData).mockResolvedValue(DATA);
    vi.mocked(downloadReportExecution).mockResolvedValue({ blob: new Blob(), fileName: "x.xlsx" });
  });

  it("generates the report once on open (even under StrictMode) without the e-mail and shows the rows", async () => {
    renderPage("/app/reports/employee.list", { strict: true });

    expect(await screen.findByText("Ayşe")).toBeInTheDocument();
    expect(screen.getByText("Mehmet")).toBeInTheDocument();
    expect(requestReport).toHaveBeenCalledTimes(1);
    expect(requestReport).toHaveBeenCalledWith("employee.list", {}, { notify: false });
  });

  it("carries the filters from the address into the request", async () => {
    vi.mocked(requestReport).mockResolvedValue({ ...EXECUTION, reportKey: "employee.departed" });

    renderPage("/app/reports/employee.departed?year=2025");

    await waitFor(() => expect(requestReport).toHaveBeenCalledWith("employee.departed", { year: "2025" }, { notify: false }));
  });

  it("restores a report from ?execution= without generating again", async () => {
    renderPage("/app/reports/employee.list?execution=exec-1");

    expect(await screen.findByText("Ayşe")).toBeInTheDocument();
    expect(getReportExecution).toHaveBeenCalledWith("exec-1");
    expect(requestReport).not.toHaveBeenCalled();
  });

  it("drops a stale or foreign ?execution= and generates the report instead", async () => {
    vi.mocked(getReportExecution).mockRejectedValue(new Error("gone"));

    renderPage("/app/reports/employee.list?execution=someone-elses");

    expect(await screen.findByText("Ayşe")).toBeInTheDocument();
    expect(requestReport).toHaveBeenCalledTimes(1);
  });

  it("shows the execution's error message when generation failed", async () => {
    vi.mocked(requestReport).mockResolvedValue({ ...EXECUTION, status: "failed", errorMessage: "boom" });

    renderPage("/app/reports/employee.list");

    expect(await screen.findByText("boom")).toBeInTheDocument();
    expect(screen.queryByText("Ayşe")).not.toBeInTheDocument();
  });

  it("downloads the same execution's Excel file from the button", async () => {
    const user = userEvent.setup();
    renderPage("/app/reports/employee.list?execution=exec-1");
    await screen.findByText("Ayşe");

    await user.click(screen.getByRole("button", { name: "Excel indir" }));

    await waitFor(() => expect(downloadReportExecution).toHaveBeenCalledWith("exec-1", "Çalışan listesi.xlsx"));
    await waitFor(() => expect(saveDownloadedReportFile).toHaveBeenCalled());
    expect(requestReport).not.toHaveBeenCalled();
  });

  it("offers only the download when the file cannot be previewed", async () => {
    const response = { status: 422, data: { code: "report_execution.preview_not_available" } } as AxiosResponse;
    vi.mocked(getReportExecutionData).mockRejectedValue(new AxiosError("bad", "ERR_BAD_REQUEST", undefined, undefined, response));

    renderPage("/app/reports/employee.list?execution=exec-1");

    expect(await screen.findByText("Bu rapor ekranda gösterilemiyor; Excel olarak indirebilirsiniz.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Excel indir" })).toBeEnabled();
  });

  it("says so when the report is not in the catalog", async () => {
    renderPage("/app/reports/unknown.report");

    expect(await screen.findByText("Bu rapor bulunamadı ya da erişiminiz yok.")).toBeInTheDocument();
    expect(requestReport).not.toHaveBeenCalled();
  });
});
