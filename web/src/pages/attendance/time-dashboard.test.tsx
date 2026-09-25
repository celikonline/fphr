import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAttendanceDaily from "../../../public/locales/tr/attendance-daily.json";
import TimeDashboardPage from "./time-dashboard";
import { useAuthStore } from "@/store/auth.store";
import { toDateOnly } from "@/lib/dates";
import {
  getAttendanceSevenDay,
  getLateEarlyByUnit,
  getOvertimeByGroup,
  getShiftAttendanceRate,
} from "@/services/analytics-time.service";

vi.mock("@/services/analytics-time.service", () => ({
  getAttendanceSevenDay: vi.fn(),
  getLateEarlyByUnit: vi.fn(),
  getOvertimeByGroup: vi.fn(),
  getShiftAttendanceRate: vi.fn(),
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
  void i18next.init({
    lng: "tr",
    fallbackLng: "tr",
    ns: ["attendance-daily"],
    resources: { tr: { "attendance-daily": trAttendanceDaily } },
    interpolation: { escapeValue: false },
  });
});

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <I18nextProvider i18n={i18next}>
        <MantineProvider>
          <TimeDashboardPage />
        </MantineProvider>
      </I18nextProvider>
    </QueryClientProvider>,
  );
}

const daysAgo = (n: number) => {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return toDateOnly(d);
};

beforeEach(() => {
  useAuthStore.setState({
    hasPermission: (p: string) => p === "attendance.dashboard.view",
    isAccessSettingEnabled: () => true,
  });
  vi.mocked(getLateEarlyByUnit).mockResolvedValue([
    { orgUnitId: "u1", orgUnitName: "Ankara", lateCount: 3, lateMinutes: 40, earlyLeaveCount: 1, earlyLeaveMinutes: 10 },
  ]);
  vi.mocked(getOvertimeByGroup).mockResolvedValue([
    { groupName: "Ankara", overtimeMinutes: 120, employeeCount: 4 },
  ]);
  vi.mocked(getShiftAttendanceRate).mockResolvedValue({ plannedShiftDays: 10, workedShiftDays: 9, rate: 0.9 });
});

afterEach(() => vi.clearAllMocks());

describe("TimeDashboardPage", () => {
  it("seven_day_chart_renders_zero_days", async () => {
    // API only returns 2 of the 7 days; the other days must still be listed with zeros.
    vi.mocked(getAttendanceSevenDay).mockResolvedValue([
      { date: daysAgo(0), planned: 10, arrived: 8, late: 1, earlyLeave: 0, absent: 1, onLeave: 1 },
      { date: daysAgo(3), planned: 10, arrived: 9, late: 0, earlyLeave: 0, absent: 0, onLeave: 1 },
    ]);
    renderPage();
    const table = await screen.findByRole("table", { name: /7 gün/i });
    await waitFor(() => expect(within(table).getAllByRole("row")).toHaveLength(8)); // header + 7 days
    expect(within(table).getAllByText("0").length).toBeGreaterThan(0);
  });

  it("group_by_switch_refetches", async () => {
    vi.mocked(getAttendanceSevenDay).mockResolvedValue([
      { date: daysAgo(0), planned: 1, arrived: 1, late: 0, earlyLeave: 0, absent: 0, onLeave: 0 },
    ]);
    renderPage();
    await waitFor(() => expect(getOvertimeByGroup).toHaveBeenCalledWith("branch"));
    await userEvent.click(await screen.findByText("Unvan"));
    await waitFor(() => expect(getOvertimeByGroup).toHaveBeenCalledWith("title"));
  });

  it("empty_scope_shows_empty_state", async () => {
    vi.mocked(getAttendanceSevenDay).mockResolvedValue([]);
    vi.mocked(getLateEarlyByUnit).mockResolvedValue([]);
    vi.mocked(getOvertimeByGroup).mockResolvedValue([]);
    vi.mocked(getShiftAttendanceRate).mockResolvedValue({ plannedShiftDays: 0, workedShiftDays: 0, rate: 0 });
    renderPage();
    expect(await screen.findByText("Bu kapsamda gösterilecek veri yok.")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });
});
