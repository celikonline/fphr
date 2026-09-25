import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAttendanceDaily from "../../../public/locales/tr/attendance-daily.json";
import trAttendance from "../../../public/locales/tr/attendance.json";
import trCommon from "../../../public/locales/tr/common.json";
import trEmployees from "../../../public/locales/tr/employees.json";
import MonthlyMatrixPage from "./monthly-matrix";
import { useAuthStore } from "@/store/auth.store";
import { toDateOnly } from "@/lib/dates";
import {
  getMonthlyMatrix,
  type MatrixCellCode,
  type MonthlyMatrix,
} from "@/services/attendance-monthly-matrix.service";

vi.mock("@/services/attendance-monthly-matrix.service", async () => {
  const actual = await vi.importActual<
    typeof import("@/services/attendance-monthly-matrix.service")
  >("@/services/attendance-monthly-matrix.service");
  return { ...actual, getMonthlyMatrix: vi.fn() };
});

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

const NOW = new Date();
const dayIso = (n: number) => toDateOnly(new Date(NOW.getFullYear(), NOW.getMonth(), n));

function matrixFixture(isLocked: boolean): MonthlyMatrix {
  return {
    isLocked,
    rows: [
      {
        employeeId: "emp-1",
        employeeName: "Ayşe Yılmaz",
        cells: [
          { date: dayIso(1), code: "full" },
          { date: dayIso(2), code: "absent" },
          { date: dayIso(3), code: "paidLeave" },
        ],
      },
    ],
  };
}

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "attendance-daily",
    ns: ["attendance-daily", "attendance", "common", "employees"],
    resources: {
      tr: {
        "attendance-daily": trAttendanceDaily,
        attendance: trAttendance,
        common: trCommon,
        employees: trEmployees,
      },
    },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider>
      <QueryClientProvider client={queryClient}>
        <I18nextProvider i18n={createTestI18n()}>
          <MonthlyMatrixPage />
        </I18nextProvider>
      </QueryClientProvider>
    </MantineProvider>
  );
}

beforeEach(() => {
  useAuthStore.setState({
    hasPermission: (p: string) => p === "attendance.daily.view",
    isAccessSettingEnabled: () => true,
  });
  vi.mocked(getMonthlyMatrix).mockResolvedValue(matrixFixture(false));
});

afterEach(() => {
  vi.clearAllMocks();
});

describe("MonthlyMatrixPage", () => {
  it("cell_colors_map_from_code", async () => {
    renderPage();
    const full = await screen.findByTestId(`matrix-cell-emp-1-${dayIso(1)}`);
    const absent = screen.getByTestId(`matrix-cell-emp-1-${dayIso(2)}`);
    const leave = screen.getByTestId(`matrix-cell-emp-1-${dayIso(3)}`);
    expect(full).toHaveAttribute("data-code", "full");
    expect(absent).toHaveAttribute("data-code", "absent");
    expect(leave).toHaveAttribute("data-code", "paidLeave");
    expect(full).toHaveAttribute("data-color", "green");
    expect(absent).toHaveAttribute("data-color", "red");
    expect(leave).toHaveAttribute("data-color", "teal");
    expect(absent.style.backgroundColor).toContain("--mantine-color-red-light");
    expect(full.style.backgroundColor).toContain("--mantine-color-green-light");
    // Renk tek başına bilgi taşımaz: hücrede kısa kod metni de var.
    expect(absent.textContent?.trim()).toBeTruthy();
    expect(absent.textContent).not.toBe(full.textContent);
  });

  it("legend_lists_all_codes", async () => {
    renderPage();
    await screen.findByTestId(`matrix-cell-emp-1-${dayIso(1)}`);
    const expected = [
      "absent",
      "missing",
      "overtime",
      "full",
      "weeklyRest",
      "restDay",
      "paidLeave",
      "unpaidLeave",
      "otherLeave",
      "publicHoliday",
      "incalculable",
      "none",
    ];
    const legendCodes = screen
      .getAllByTestId(/^matrix-legend-/)
      .map((el) => el.getAttribute("data-testid")!.replace("matrix-legend-", ""));
    expect([...legendCodes].sort()).toEqual([...expected].sort());
  });

  it("cell_has_accessible_name_with_employee_date_and_code", async () => {
    renderPage();
    await screen.findByTestId(`matrix-cell-emp-1-${dayIso(2)}`);
    const table = screen.getByRole("table", {
      name: new RegExp(`^${trAttendanceDaily.monthlyMatrix.title}`),
    });
    const cells = within(table).getAllByRole("img");
    const target = cells.find(
      (el) => el.getAttribute("data-testid") === `matrix-cell-emp-1-${dayIso(2)}`
    )!;
    const name = target.getAttribute("aria-label") ?? "";
    expect(name).toContain("Ayşe Yılmaz");
    expect(name).toContain(trAttendanceDaily.monthlyMatrix.codes.absent);
    expect(name).toContain(String(new Date(NOW.getFullYear(), NOW.getMonth(), 2).getFullYear()));
    expect(screen.getByRole("rowheader", { name: "Ayşe Yılmaz" })).toBeInTheDocument();
    expect(screen.getAllByRole("columnheader").length).toBeGreaterThan(28);
  });

  it("unknown_code_falls_back_to_none", async () => {
    const data = matrixFixture(false);
    data.rows[0].cells[0] = { date: dayIso(1), code: "brandNew" as MatrixCellCode };
    vi.mocked(getMonthlyMatrix).mockResolvedValue(data);
    renderPage();
    const cell = await screen.findByTestId(`matrix-cell-emp-1-${dayIso(1)}`);
    expect(cell).toHaveAttribute("data-code", "none");
    expect(cell).toHaveAttribute("data-color", "gray");
  });

  it("day_headers_include_weekday", async () => {
    renderPage();
    await screen.findByTestId(`matrix-cell-emp-1-${dayIso(1)}`);
    const weekday = new Intl.DateTimeFormat("tr", { weekday: "short" }).format(
      new Date(NOW.getFullYear(), NOW.getMonth(), 1)
    );
    const header = screen.getAllByRole("columnheader")[1];
    expect(header.textContent).toContain(weekday);
  });

  it("no_access_without_permission", () => {
    useAuthStore.setState({ hasPermission: () => false, isAccessSettingEnabled: () => true });
    renderPage();
    expect(screen.getByText(trCommon.noAccessTitle)).toBeInTheDocument();
    expect(getMonthlyMatrix).not.toHaveBeenCalled();
  });

  it("locked_badge_shown", async () => {
    vi.mocked(getMonthlyMatrix).mockResolvedValue(matrixFixture(true));
    renderPage();
    expect(await screen.findByTestId("matrix-locked-badge")).toBeInTheDocument();
  });

  it("locked_badge_hidden_when_open", async () => {
    renderPage();
    await screen.findByTestId(`matrix-cell-emp-1-${dayIso(1)}`);
    await waitFor(() => expect(screen.queryByTestId("matrix-locked-badge")).toBeNull());
  });
});
