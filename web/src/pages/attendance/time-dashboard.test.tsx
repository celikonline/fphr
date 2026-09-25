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
import { formatDateOnly, toDateOnly } from "@/lib/dates";
import { sevenDayRange } from "@/components/attendance/time-kpi-seven-day";
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
    const rowFor = (n: number) =>
      within(table)
        .getAllByRole("row")
        .find((r) => within(r).queryByText(formatDateOnly(daysAgo(n), "tr", { day: "2-digit", month: "short" })));
    const cells = (n: number) => within(rowFor(n)!).getAllByRole("cell").map((c) => c.textContent);
    // date, planned, arrived, late, earlyLeave, absent, onLeave
    expect(cells(1).slice(1)).toEqual(["0", "0", "0", "0", "0", "0"]);
    expect(cells(6).slice(1)).toEqual(["0", "0", "0", "0", "0", "0"]);
    expect(cells(0).slice(1)).toEqual(["10", "8", "1", "0", "1", "1"]);
    expect(cells(3).slice(1)).toEqual(["10", "9", "0", "0", "0", "1"]);
  });

  it("sends_client_seven_day_range_to_endpoints", async () => {
    vi.mocked(getAttendanceSevenDay).mockResolvedValue([]);
    renderPage();
    const range = { from: daysAgo(6), to: daysAgo(0) };
    expect(sevenDayRange(new Date())).toEqual(range);
    await waitFor(() => expect(getAttendanceSevenDay).toHaveBeenCalledWith(range));
    await waitFor(() => expect(getLateEarlyByUnit).toHaveBeenCalledWith(range));
    await waitFor(() => expect(getOvertimeByGroup).toHaveBeenCalledWith("branch", range));
    await waitFor(() => expect(getShiftAttendanceRate).toHaveBeenCalledWith(range));
  });

  it("group_charts_have_text_alternative_tables", async () => {
    vi.mocked(getAttendanceSevenDay).mockResolvedValue([
      { date: daysAgo(0), planned: 1, arrived: 1, late: 0, earlyLeave: 0, absent: 0, onLeave: 0 },
    ]);
    renderPage();
    const lateEarly = await screen.findByRole("table", { name: /geç kalma/i });
    const lateRows = within(lateEarly).getAllByRole("row");
    expect(lateRows).toHaveLength(2);
    expect(within(lateRows[1]).getAllByRole("cell").map((c) => c.textContent)).toEqual(["Ankara", "3", "1"]);
    const overtime = await screen.findByRole("table", { name: /fazla mesai/i });
    const otRows = within(overtime).getAllByRole("row");
    expect(otRows).toHaveLength(2);
    expect(within(otRows[1]).getAllByRole("cell").map((c) => c.textContent)).toEqual(["Ankara", "2"]);
  });

  it("group_by_switch_refetches", async () => {
    vi.mocked(getAttendanceSevenDay).mockResolvedValue([
      { date: daysAgo(0), planned: 1, arrived: 1, late: 0, earlyLeave: 0, absent: 0, onLeave: 0 },
    ]);
    renderPage();
    await waitFor(() => expect(getOvertimeByGroup).toHaveBeenCalledWith("branch", expect.anything()));
    await userEvent.click(await screen.findByText("Unvan"));
    await waitFor(() => expect(getOvertimeByGroup).toHaveBeenCalledWith("title", expect.anything()));
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
