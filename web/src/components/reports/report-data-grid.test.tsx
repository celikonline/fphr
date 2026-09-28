import { beforeAll, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MantineProvider } from "@mantine/core";
import trReports from "../../../public/locales/tr/reports.json";
import trCommon from "../../../public/locales/tr/common.json";
import type { ReportData } from "@/services/reports.service";
import { ReportDataGrid } from "./report-data-grid";

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

function pagedRows(items: ReportData["rows"]["items"], totalCount = items.length): ReportData["rows"] {
  return { items, page: 1, pageSize: 50, totalCount, totalPages: Math.max(1, Math.ceil(totalCount / 50)), hasNext: false, hasPrevious: false };
}

const DATA: ReportData = {
  sheets: [{ index: 0, name: "Kişiler" }],
  sheetIndex: 0,
  columns: [
    { header: "Ad", type: "text" },
    { header: "Maaş", type: "number" },
    { header: "İşe giriş", type: "date" },
    { header: "Aktif", type: "boolean" },
  ],
  rows: pagedRows([
    ["Ayşe", 45000.5, "2024-03-01", true],
    ["Mehmet", null, "2023-11-15", false],
  ]),
};

function renderGrid(overrides: Partial<React.ComponentProps<typeof ReportDataGrid>> = {}) {
  const props: React.ComponentProps<typeof ReportDataGrid> = {
    data: DATA,
    isLoading: false,
    isFetching: false,
    search: "",
    onSearchChange: vi.fn(),
    sorting: [],
    onSortingChange: vi.fn(),
    onSheetChange: vi.fn(),
    onPageChange: vi.fn(),
    pageSize: 50,
    onPageSizeChange: vi.fn(),
    ...overrides,
  };
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
  render(
    <I18nextProvider i18n={instance}>
      <MantineProvider>
        <ReportDataGrid {...props} />
      </MantineProvider>
    </I18nextProvider>,
  );
  return props;
}

describe("ReportDataGrid", () => {
  it("renders headers and formats numbers, dates and booleans", () => {
    renderGrid();

    expect(screen.getByRole("columnheader", { name: /Maaş/ })).toBeInTheDocument();
    expect(screen.getByText("Ayşe")).toBeInTheDocument();
    expect(screen.getByText("45.000,5")).toBeInTheDocument();
    expect(screen.getByText(/2024/)).toBeInTheDocument();
    expect(screen.getByText("Evet")).toBeInTheDocument();
    expect(screen.getByText("Hayır")).toBeInTheDocument();
  });

  it("reports a sort request by column index when a header is clicked", async () => {
    const user = userEvent.setup();
    const props = renderGrid();

    await user.click(screen.getByRole("button", { name: /Maaş/ }));

    expect(props.onSortingChange).toHaveBeenCalledWith([{ id: "1", desc: false }]);
  });

  it("reports typed search text", async () => {
    const user = userEvent.setup();
    const props = renderGrid();

    await user.type(screen.getByPlaceholderText("Sonuçlarda ara"), "a");

    expect(props.onSearchChange).toHaveBeenCalledWith("a");
  });

  it("shows sheet tabs only when there are several sheets and reports the chosen one", async () => {
    const user = userEvent.setup();
    const props = renderGrid({ data: { ...DATA, sheets: [{ index: 0, name: "Kişiler" }, { index: 1, name: "Özet" }] } });

    await user.click(screen.getByRole("tab", { name: "Özet" }));

    expect(props.onSheetChange).toHaveBeenCalledWith(1);
  });

  it("does not show sheet tabs for a single sheet", () => {
    renderGrid();

    expect(screen.queryByRole("tab")).not.toBeInTheDocument();
  });

  it("says there are no records and hides the pager for an empty report", () => {
    renderGrid({ data: { ...DATA, rows: pagedRows([]) } });

    expect(screen.getByText("Bu filtrelerle gösterilecek kayıt yok.")).toBeInTheDocument();
    expect(screen.queryByRole("navigation")).not.toBeInTheDocument();
  });
});
