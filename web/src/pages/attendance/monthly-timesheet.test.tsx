import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError } from "axios";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAttendance from "../../../public/locales/tr/attendance.json";
import trCommon from "../../../public/locales/tr/common.json";
import trEmployees from "../../../public/locales/tr/employees.json";
import MonthlyTimesheetPage from "./monthly-timesheet";
import { useAuthStore } from "@/store/auth.store";
import { toDateOnly } from "@/lib/dates";
import { toast } from "@/hooks/use-toast";
import {
  approveAllMonthlyTimesheets,
  approveMonthlyTimesheet,
  getMonthlyTimesheet,
  getTeamMonth,
  type MonthlyTimesheetRow,
  type TeamWeekRow,
} from "@/services/attendance.service";

vi.mock("@/services/attendance.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/attendance.service")>(
    "@/services/attendance.service"
  );
  return {
    ...actual,
    getMonthlyTimesheet: vi.fn(),
    getTeamMonth: vi.fn(),
    approveMonthlyTimesheet: vi.fn(),
    approveAllMonthlyTimesheets: vi.fn(),
  };
});

vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

// The org-unit filter is unrelated here; stub its data source so the test does not hit the network.
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
const MONTH_ISO = toDateOnly(new Date(NOW.getFullYear(), NOW.getMonth(), 1));
const dayIso = (n: number) => toDateOnly(new Date(NOW.getFullYear(), NOW.getMonth(), n));

function setPermissions(granted: string[]) {
  useAuthStore.setState({
    hasPermission: (permission: string) => granted.includes(permission),
    isAccessSettingEnabled: () => true,
  });
}

function rowsFixture(): MonthlyTimesheetRow[] {
  return [
    {
      employeeId: "emp-2",
      employeeName: "Baran Demir",
      workedDays: 20,
      workedHours: 160.5,
      missingDays: 0,
      overtimeHours: 2,
      paidLeaveDays: 1,
      unpaidLeaveDays: 0,
      sickLeaveDays: 0,
      isPeriodLocked: false,
      isApproved: true,
      approvedAt: "2026-09-30T10:00:00Z",
    },
    {
      employeeId: "emp-1",
      employeeName: "Ayşe Yılmaz",
      workedDays: 18,
      workedHours: 144,
      missingDays: 2,
      overtimeHours: 0,
      paidLeaveDays: 2,
      unpaidLeaveDays: 1,
      sickLeaveDays: 0.5,
      isPeriodLocked: false,
      isApproved: false,
    },
  ];
}

function daysFixture(): TeamWeekRow[] {
  const base = {
    breakMinutesApplied: 0,
    nightMinutes: 0,
    dailyLimitExceeded: false,
    nightLimitExceeded: false,
  };
  return [
    {
      employeeId: "emp-1",
      employeeName: "Ayşe Yılmaz",
      days: [
        { ...base, date: dayIso(1), workedMinutes: 480, status: "unresolved", anomalies: [] },
        {
          ...base,
          date: dayIso(2),
          workedMinutes: 480,
          status: "unresolved",
          anomalies: ["workedWhileOnLeave"],
        },
        {
          ...base,
          date: dayIso(3),
          workedMinutes: 0,
          status: "unresolved",
          anomalies: ["missingCheckIn"],
        },
      ],
    },
    {
      employeeId: "emp-2",
      employeeName: "Baran Demir",
      days: [{ ...base, date: dayIso(1), workedMinutes: 480, status: "unresolved", anomalies: [] }],
    },
  ];
}

beforeEach(() => {
  setPermissions(["attendance.timesheet.view", "attendance.timesheet.approve"]);
  vi.mocked(getMonthlyTimesheet).mockResolvedValue(rowsFixture());
  vi.mocked(getTeamMonth).mockResolvedValue(daysFixture());
});

afterEach(() => {
  vi.clearAllMocks();
});

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "attendance",
    ns: ["attendance", "common", "employees"],
    resources: { tr: { attendance: trAttendance, common: trCommon, employees: trEmployees } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider env="test">
          <MonthlyTimesheetPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("MonthlyTimesheetPage", () => {
  it("renders one sorted row per employee with totals, approval state and anomaly badges", async () => {
    renderPage();

    expect(await screen.findByText("Ayşe Yılmaz")).toBeInTheDocument();
    const rowTexts = screen.getAllByRole("row").map((r) => r.textContent ?? "");
    expect(rowTexts.findIndex((t) => t.includes("Ayşe"))).toBeLessThan(
      rowTexts.findIndex((t) => t.includes("Baran"))
    );
    expect(vi.mocked(getMonthlyTimesheet)).toHaveBeenCalledWith(MONTH_ISO, null);
    expect(screen.getByText("Onaylandı")).toBeInTheDocument();
    expect(screen.getByText("Onay bekliyor")).toBeInTheDocument();
    expect(await screen.findByText("2 gün uyuşmazlık")).toBeInTheDocument();
    expect(screen.getByText(/Worked while on leave|İzinli günde çalışma/i)).toBeInTheDocument();
    expect(screen.getByText("1 / 2 çalışan onay bekliyor")).toBeInTheDocument();
  });

  it("expands a row into the day strip and flags the worked-while-on-leave day", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ayşe Yılmaz");

    await user.click(await screen.findByRole("button", { name: /Ayşe Yılmaz gün detayını/ }));

    const strip = await screen.findByTestId("day-strip");
    expect(within(strip).getAllByText(/^\d{2}$/)).toHaveLength(3);
    expect(strip.querySelector('[data-tone="leaveConflict"]')).not.toBeNull();
    expect(strip.querySelector('[data-tone="issue"]')).not.toBeNull();
    expect(strip.querySelector('[data-tone="ok"]')).not.toBeNull();
  });

  it("asks for confirmation before approve-all and then calls the service for the visible month", async () => {
    const user = userEvent.setup();
    vi.mocked(approveAllMonthlyTimesheets).mockResolvedValue(1);
    renderPage();
    await screen.findByText("Ayşe Yılmaz");

    await user.click(screen.getByRole("button", { name: "Tümünü Onayla" }));
    expect(approveAllMonthlyTimesheets).not.toHaveBeenCalled();

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/1 çalışanın puantajı onaylanacak/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: "Tümünü Onayla" }));

    await waitFor(() => expect(approveAllMonthlyTimesheets).toHaveBeenCalledWith(MONTH_ISO, null));
    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({
          variant: "success",
          description: "1 çalışanın puantajı onaylandı.",
        })
      )
    );
  });

  it("approves a single employee via the row button (already approved rows offer re-approve)", async () => {
    const user = userEvent.setup();
    vi.mocked(approveMonthlyTimesheet).mockResolvedValue({
      employeeId: "emp-1",
      periodMonth: MONTH_ISO,
      approvedAt: "2026-09-30T10:00:00Z",
    });
    renderPage();
    await screen.findByText("Ayşe Yılmaz");

    expect(screen.getByRole("button", { name: /Baran Demir puantajını onayla/ })).toHaveTextContent(
      "Yeniden onayla"
    );
    await user.click(screen.getByRole("button", { name: /Ayşe Yılmaz puantajını onayla/ }));

    await waitFor(() => expect(approveMonthlyTimesheet).toHaveBeenCalledWith("emp-1", MONTH_ISO));
  });

  it("shows the localized forbidden message when the server answers 403", async () => {
    const user = userEvent.setup();
    vi.mocked(approveMonthlyTimesheet).mockRejectedValue(
      new AxiosError("forbidden", "ERR_BAD_REQUEST", undefined, undefined, {
        status: 403,
        data: {},
      } as never)
    );
    renderPage();
    await screen.findByText("Ayşe Yılmaz");

    await user.click(screen.getByRole("button", { name: /Ayşe Yılmaz puantajını onayla/ }));

    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({
          variant: "destructive",
          description: expect.stringContaining("yetkiniz yok"),
        })
      )
    );
  });

  it("surfaces the server's validation detail on a 400", async () => {
    const user = userEvent.setup();
    vi.mocked(approveMonthlyTimesheet).mockRejectedValue(
      new AxiosError("bad", "ERR_BAD_REQUEST", undefined, undefined, {
        status: 400,
        data: { detail: "Dönem geçersiz." },
      } as never)
    );
    renderPage();
    await screen.findByText("Ayşe Yılmaz");

    await user.click(screen.getByRole("button", { name: /Ayşe Yılmaz puantajını onayla/ }));

    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "destructive", description: "Dönem geçersiz." })
      )
    );
  });

  it("hides every approve action without attendance.timesheet.approve but still shows the grid", async () => {
    setPermissions(["attendance.timesheet.view"]);
    renderPage();

    expect(await screen.findByText("Ayşe Yılmaz")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Tümünü Onayla" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /puantajını onayla/ })).not.toBeInTheDocument();
    expect(screen.getByText(/yalnızca görüntüleyebilirsiniz/)).toBeInTheDocument();
  });

  it("keeps the totals grid usable when the day-level query fails", async () => {
    vi.mocked(getTeamMonth).mockRejectedValue(new Error("boom"));
    const user = userEvent.setup();
    renderPage();

    expect(await screen.findByText("Ayşe Yılmaz")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Ayşe Yılmaz gün detayını/ }));
    expect(await screen.findByText("Gün detayı yüklenemedi.")).toBeInTheDocument();
  });

  it("disables Tümünü Onayla when every row is already approved", async () => {
    vi.mocked(getMonthlyTimesheet).mockResolvedValue(
      rowsFixture().map((r) => ({ ...r, isApproved: true }))
    );
    renderPage();
    await screen.findByText("Ayşe Yılmaz");

    expect(screen.getByRole("button", { name: "Tümünü Onayla" })).toBeDisabled();
  });

  it("renders NoAccess without the view permission and never queries the grid", () => {
    setPermissions([]);
    renderPage();

    expect(getMonthlyTimesheet).not.toHaveBeenCalled();
    expect(screen.queryByText("Ayşe Yılmaz")).not.toBeInTheDocument();
  });
});
