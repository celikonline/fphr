import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor } from "@testing-library/react";
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
  MATRIX_CELL_CODES,
  type MonthlyMatrix,
} from "@/services/attendance-monthly-matrix.service";

vi.mock("@/services/attendance-monthly-matrix.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/attendance-monthly-matrix.service")>(
    "@/services/attendance-monthly-matrix.service"
  );
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
    expect(full.getAttribute("data-color")).not.toBe(absent.getAttribute("data-color"));
    // Renk tek başına bilgi taşımaz: hücrede kısa kod metni de var.
    expect(absent.textContent?.trim()).toBeTruthy();
    expect(absent.textContent).not.toBe(full.textContent);
  });

  it("legend_lists_all_codes", async () => {
    renderPage();
    await screen.findByTestId(`matrix-cell-emp-1-${dayIso(1)}`);
    for (const code of MATRIX_CELL_CODES) {
      expect(screen.getByTestId(`matrix-legend-${code}`)).toBeInTheDocument();
    }
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
