import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trPayroll from "../../../public/locales/tr/payroll.json";
import trCommon from "../../../public/locales/tr/common.json";
import PayslipImportPage from "./payslip-import";
import { importPayslipFiles, type PayslipImportResult } from "@/services/payslip-imports.service";

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return { ...actual, usePermission: () => true };
});

const RESULT: PayslipImportResult = {
  total: 6,
  imported: 1,
  files: [
    { fileName: "1001_2026-08.pdf", status: "imported", employeeId: "e1", period: "2026-08", documentId: "d1" },
    { fileName: "9999_2026-08.pdf", status: "unmatched", reason: "unknown_registration", period: "2026-08" },
    { fileName: "1002_2026-08.pdf", status: "duplicate", reason: "duplicate_registration", period: "2026-08" },
    { fileName: "1003_2026-08.pdf", status: "terminated", reason: "terminated_employee", period: "2026-08" },
    { fileName: "notes.txt", status: "skipped", reason: "not_pdf" },
    { fileName: "abc.pdf", status: "skipped", reason: "invalid_file_name" },
  ],
};

vi.mock("@/services/payslip-imports.service", () => ({ importPayslipFiles: vi.fn(async () => RESULT) }));

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
          <PayslipImportPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("PayslipImportPage", () => {
  it("import_result_table_lists_each_file", async () => {
    renderPage();
    const user = userEvent.setup();
    const pdf = new File(["x"], "1001_2026-08.pdf", { type: "application/pdf" });
    const zip = new File(["y"], "hepsi.zip", { type: "application/zip" });

    await user.upload(screen.getByTestId("payslip-files"), [pdf, zip]);
    await user.click(screen.getByRole("button", { name: "Yükle ve dağıt" }));

    await waitFor(() => expect(importPayslipFiles).toHaveBeenCalledWith([pdf, zip]));
    const table = await screen.findByRole("table");
    const rows = within(table).getAllByRole("row");
    expect(rows).toHaveLength(RESULT.files.length + 1);
    for (const f of RESULT.files) expect(within(table).getByText(f.fileName)).toBeTruthy();
    expect(within(table).getByText("Eşleşti")).toBeTruthy();
    expect(within(table).getByText("Eşleşmedi")).toBeTruthy();
    expect(within(table).getByText("Çift")).toBeTruthy();
    expect(within(table).getByText("Ayrılmış çalışan")).toBeTruthy();
    expect(within(table).getByText("PDF değil")).toBeTruthy();
  });
});
