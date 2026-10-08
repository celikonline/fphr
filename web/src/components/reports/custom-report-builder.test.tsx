import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trReports from "../../../public/locales/tr/reports.json";
import trCommon from "../../../public/locales/tr/common.json";
import { CustomReportBuilder } from "./custom-report-builder";
import { getCustomReportCatalog, requestReport } from "@/services/reports.service";

vi.mock("@/services/reports.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/reports.service")>("@/services/reports.service");
  return { ...actual, getCustomReportCatalog: vi.fn(), requestReport: vi.fn() };
});
vi.mock("@/components/employees/use-org-units", () => ({ useOrgUnits: () => ({ options: [], nameById: new Map(), isLoading: false }) }));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: () => ({ matches: false, media: "", onchange: null, addListener: () => {}, removeListener: () => {}, addEventListener: () => {}, removeEventListener: () => {}, dispatchEvent: () => false }),
  });
  window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
});

function renderBuilder() {
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
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <MantineProvider><CustomReportBuilder /></MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

const catalog = {
  groups: [
    { key: "employee.general", labelKey: "reports:groups.employee.general", sortOrder: 0 },
    { key: "leave.requests", labelKey: "reports:groups.leave.requests", sortOrder: 3 },
    { key: "leave.overtime", labelKey: "reports:groups.leave.overtime", sortOrder: 5 },
  ],
  fields: [
    { key: "employee.firstName", groupKey: "employee.general", labelKey: "reports:fields.employee.firstName", valueType: "text" as const, grain: "employee" as const, sortOrder: 10 },
    { key: "leave.request.days", groupKey: "leave.requests", labelKey: "reports:fields.leave.request.days", valueType: "number" as const, grain: "detail" as const, sortOrder: 10 },
    { key: "leave.overtime.workDate", groupKey: "leave.overtime", labelKey: "reports:fields.leave.overtime.workDate", valueType: "date" as const, grain: "detail" as const, sortOrder: 10 },
  ],
};

describe("CustomReportBuilder", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getCustomReportCatalog).mockResolvedValue(catalog);
    vi.mocked(requestReport).mockResolvedValue({ id: "queued-1", reportKey: "report.custom", status: "queued", requestedAt: "2026-10-08T10:00:00Z" });
  });

  it("loads authorized fields, prevents incompatible detail grains, and submits a bounded selection", async () => {
    const user = userEvent.setup();
    renderBuilder();

    await user.click(screen.getByRole("button", { name: "Özel rapor oluştur" }));
    const nameField = await screen.findByRole("checkbox", { name: "Ad" });
    const leaveDays = screen.getByRole("checkbox", { name: "Gün sayısı" });
    const overtimeDate = screen.getByRole("checkbox", { name: "Çalışma tarihi" });

    await user.click(nameField);
    await user.click(leaveDays);
    expect(overtimeDate).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Excel raporu oluştur" }));

    await waitFor(() => expect(requestReport).toHaveBeenCalledOnce());
    const [key, parameters] = vi.mocked(requestReport).mock.calls[0];
    expect(key).toBe("report.custom");
    expect(JSON.parse(parameters!.fieldKeys)).toEqual(["employee.firstName", "leave.request.days"]);
    expect(parameters!.from).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(parameters!.to).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});
