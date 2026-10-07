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

const permissions = vi.hoisted(() => ({ denied: new Set<string>() }));

vi.mock("@/components/permission-guard", () => ({
  usePermission: (permission: string) => !permissions.denied.has(permission),
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
    permissions.denied.clear();
    vi.mocked(getReportCatalog).mockResolvedValue([NO_FILTER, WITH_YEAR]);
    vi.mocked(requestReport).mockResolvedValue(EXECUTION);
    vi.mocked(getReportExecution).mockResolvedValue(EXECUTION);
    vi.mocked(getReportExecutionData).mockResolvedValue(DATA);
    vi.mocked(downloadReportExecution).mockResolvedValue({ blob: new Blob(), fileName: "x.xlsx" });
  });


  it("polls an asynchronous leave detail run, keeps the grid hidden until ready and requests its ready email", async () => {
    const detail: ReportCatalogItem = { key: "leave.balance.detail", categoryKey: "administrative", parameters: [], executionMode: "async" };
    vi.mocked(getReportCatalog).mockResolvedValue([detail]);
    const queued: ReportExecution = { ...EXECUTION, id: "detail-1", reportKey: detail.key, status: "queued", resultFileName: null };
    vi.mocked(requestReport).mockResolvedValue(queued);
    vi.mocked(getReportExecution).mockResolvedValueOnce(queued).mockResolvedValueOnce({ ...queued, status: "running" }).mockResolvedValue({ ...queued, status: "succeeded", resultFileName: "Detay.xlsx" });
    renderPage("/app/reports/leave.balance.detail");
    await waitFor(() => expect(requestReport).toHaveBeenCalledWith(detail.key, {}, { notify: true }));
    expect(await screen.findByText("Rapor sıraya alındı. Hazırlanması bir dakika sürebilir.")).toBeInTheDocument();
    expect(getReportExecutionData).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Excel indir" })).toBeDisabled();
    expect(await screen.findByText("Ayşe", {}, { timeout: 7000 })).toBeInTheDocument();
    expect(getReportExecutionData).toHaveBeenCalledWith("detail-1", expect.anything());
  });

  it("stops polling and enables retry when an asynchronous execution status request fails", async () => {
    const detail: ReportCatalogItem = { key: "leave.balance.detail", categoryKey: "administrative", parameters: [], executionMode: "async" };
    vi.mocked(getReportCatalog).mockResolvedValue([detail]);
    const queued: ReportExecution = { ...EXECUTION, id: "detail-1", reportKey: detail.key, status: "queued", resultFileName: null };
    vi.mocked(requestReport).mockResolvedValue(queued);
    vi.mocked(getReportExecution).mockRejectedValue(new Error("status unavailable"));
    renderPage("/app/reports/leave.balance.detail");

    expect(await screen.findByText("Rapor sıraya alındı. Hazırlanması bir dakika sürebilir.")).toBeInTheDocument();
    expect(await screen.findByRole("alert")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Uygula" })).toBeEnabled();
    await waitFor(() => expect(getReportExecution).toHaveBeenCalledTimes(1));
    await new Promise((resolve) => setTimeout(resolve, 1300));
    expect(getReportExecution).toHaveBeenCalledTimes(1);
  });

  it("downloads the PDF from the selected execution snapshot", async () => {
    const user = userEvent.setup();
    const detail: ReportCatalogItem = { key: "leave.balance.detail", categoryKey: "administrative", parameters: [], executionMode: "async" };
    vi.mocked(getReportCatalog).mockResolvedValue([detail]);
    vi.mocked(getReportExecution).mockResolvedValue({
      ...EXECUTION,
      id: "detail-1",
      reportKey: detail.key,
      status: "succeeded",
      resultFileName: "Detay.xlsx",
    });
    renderPage("/app/reports/leave.balance.detail?execution=detail-1");
    await screen.findByText("Ayşe");
    await user.click(screen.getByRole("button", { name: "PDF indir" }));

    await waitFor(() => expect(downloadReportExecution).toHaveBeenCalledWith("detail-1", `${detail.key}.pdf`, "pdf"));
    expect(requestReport).not.toHaveBeenCalled();
    expect(saveDownloadedReportFile).toHaveBeenCalled();
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

  it("downloads a PDF from the selected report execution", async () => {
    const user = userEvent.setup();
    vi.mocked(requestReport).mockResolvedValue({
      ...EXECUTION,
      id: "employee-pdf-1",
      resultFileName: "Çalışan listesi.pdf",
    });
    renderPage("/app/reports/employee.list?execution=exec-1");
    await screen.findByText("Ayşe");

    await user.click(screen.getByRole("button", { name: "PDF indir" }));

    await waitFor(() => expect(requestReport).toHaveBeenCalledWith("employee.list", { format: "pdf" }, { notify: false }));
    await waitFor(() => expect(downloadReportExecution).toHaveBeenCalledWith("employee-pdf-1", "Çalışan listesi.pdf"));
    expect(saveDownloadedReportFile).toHaveBeenCalled();
  });

  it("draws a select parameter, carries its value, and does not draw the format parameter", async () => {
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
    vi.mocked(requestReport).mockResolvedValue({ ...EXECUTION, reportKey: "attendance.time.daily_status" });

    renderPage("/app/reports/attendance.time.daily_status?status=absent");

    expect((await screen.findAllByLabelText("Durum *")).length).toBeGreaterThan(0);
    expect(screen.queryByLabelText(/^Biçim|^format/i)).not.toBeInTheDocument();
    await waitFor(() =>
      expect(requestReport).toHaveBeenCalledWith("attendance.time.daily_status", { status: "absent" }, { notify: false }),
    );
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

  it("shows the load error, not the empty-result text, when the report data cannot be fetched", async () => {
    const response = { status: 500, data: {} } as AxiosResponse;
    vi.mocked(getReportExecutionData).mockRejectedValue(new AxiosError("boom", "ERR_BAD_RESPONSE", undefined, undefined, response));

    renderPage("/app/reports/employee.list?execution=exec-1");

    expect(await screen.findByText("Rapor verisi yüklenemedi.")).toBeInTheDocument();
    expect(screen.queryByText("Bu filtrelerle gösterilecek kayıt yok.")).not.toBeInTheDocument();
    expect(screen.queryByText("Bu rapor ekranda gösterilemiyor; Excel olarak indirebilirsiniz.")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Excel indir" })).toBeEnabled();
  });

  it("shows the load error for a plain network failure too", async () => {
    vi.mocked(getReportExecutionData).mockRejectedValue(new Error("timeout"));

    renderPage("/app/reports/employee.list?execution=exec-1");

    expect(await screen.findByText("Rapor verisi yüklenemedi.")).toBeInTheDocument();
    expect(screen.queryByText("Bu filtrelerle gösterilecek kayıt yok.")).not.toBeInTheDocument();
  });

  it("searches the rows after the debounce, and sends no q for an empty or whitespace-only box", async () => {
    const user = userEvent.setup();
    renderPage("/app/reports/employee.list?execution=exec-1");
    await screen.findByText("Ayşe");
    const box = screen.getByRole("textbox", { name: "Sonuçlarda ara" });

    await user.type(box, "Ay");
    await waitFor(() => expect(getReportExecutionData).toHaveBeenCalledWith("exec-1", expect.objectContaining({ q: "Ay", page: 1 })), {
      timeout: 3000,
    });

    await user.clear(box);
    await user.type(box, "   ");
    await waitFor(
      () => {
        const [, query] = vi.mocked(getReportExecutionData).mock.lastCall ?? [];
        expect(query).toEqual(expect.objectContaining({ page: 1 }));
        expect(query?.q).toBeUndefined();
      },
      { timeout: 3000 }
    );
  });

  it("sorts by the clicked column: ascending first, then descending", async () => {
    const user = userEvent.setup();
    renderPage("/app/reports/employee.list?execution=exec-1");
    await screen.findByText("Ayşe");

    await user.click(screen.getByRole("button", { name: "Ad" }));
    await waitFor(() => expect(getReportExecutionData).toHaveBeenCalledWith("exec-1", expect.objectContaining({ sort: "0", page: 1 })));

    await user.click(screen.getByRole("button", { name: "Ad" }));
    await waitFor(() => expect(getReportExecutionData).toHaveBeenCalledWith("exec-1", expect.objectContaining({ sort: "-0", page: 1 })));
  });

  it("requests the next page from the pager", async () => {
    const user = userEvent.setup();
    vi.mocked(getReportExecutionData).mockResolvedValue({
      ...DATA,
      rows: { items: [["Ayşe"], ["Mehmet"]], page: 1, pageSize: 50, totalCount: 120, totalPages: 3, hasNext: true, hasPrevious: false },
    });
    renderPage("/app/reports/employee.list?execution=exec-1");
    await screen.findByText("Ayşe");

    await user.click(screen.getByRole("button", { name: "2" }));

    await waitFor(() => expect(getReportExecutionData).toHaveBeenCalledWith("exec-1", expect.objectContaining({ page: 2, pageSize: 50 })));
  });

  it("generates again from Uygula and then downloads the new execution's file", async () => {
    const user = userEvent.setup();
    const regenerated: ReportExecution = { ...EXECUTION, id: "exec-2", resultFileName: "Yeni.xlsx" };
    vi.mocked(getReportExecution).mockImplementation((id: string) => Promise.resolve(id === "exec-2" ? regenerated : EXECUTION));
    vi.mocked(requestReport).mockResolvedValue(regenerated);
    renderPage("/app/reports/employee.list?execution=exec-1");
    await screen.findByText("Ayşe");
    expect(requestReport).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Uygula" }));

    await waitFor(() => expect(requestReport).toHaveBeenCalledWith("employee.list", {}, { notify: false }));
    await waitFor(() => expect(getReportExecutionData).toHaveBeenCalledWith("exec-2", expect.anything()));
    await user.click(screen.getByRole("button", { name: "Excel indir" }));

    await waitFor(() => expect(downloadReportExecution).toHaveBeenCalledWith("exec-2", "Yeni.xlsx"));
    expect(requestReport).toHaveBeenCalledTimes(1);
  });

  it("drops an ?execution= that belongs to another report and generates this one", async () => {
    vi.mocked(getReportExecution).mockImplementation((id: string) =>
      Promise.resolve(id === "foreign" ? { ...EXECUTION, id, reportKey: "employee.departed" } : { ...EXECUTION, id })
    );

    renderPage("/app/reports/employee.list?execution=foreign");

    expect(await screen.findByText("Ayşe")).toBeInTheDocument();
    expect(requestReport).toHaveBeenCalledTimes(1);
    expect(requestReport).toHaveBeenCalledWith("employee.list", {}, { notify: false });
    expect(getReportExecutionData).not.toHaveBeenCalledWith("foreign", expect.anything());
  });

  it.each(["reports.my.executions", "reports.catalog.view"])("shows the no-access page without generating when %s is missing", async (permission) => {
    permissions.denied.add(permission);

    renderPage("/app/reports/employee.list");

    expect(await screen.findByText("Bu sayfaya erişiminiz yok")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Excel indir" })).not.toBeInTheDocument();
    expect(requestReport).not.toHaveBeenCalled();
    if (permission === "reports.catalog.view") expect(getReportCatalog).not.toHaveBeenCalled();
  });
});
