import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError } from "axios";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trDaily from "../../../public/locales/tr/attendance-daily.json";
import trCommon from "../../../public/locales/tr/common.json";
import DailyListPage from "./daily-list";
import { useAuthStore } from "@/store/auth.store";
import { addDaysToDateOnly, toDateOnly } from "@/lib/dates";
import {
  addManualPunch,
  deletePunch,
  getDailyCounts,
  listDaily,
  type DailyAttendanceRow,
} from "@/services/attendance-daily.service";

vi.mock("@/services/attendance-daily.service", () => ({
  listDaily: vi.fn(),
  getDailyCounts: vi.fn(),
  addManualPunch: vi.fn(),
  deletePunch: vi.fn(),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));
vi.mock("@/components/employees/use-org-units", () => ({
  useOrgUnits: () => ({ options: [], nameById: new Map(), isLoading: false }),
}));

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({
      matches: false, media: query, onchange: null,
      addListener: () => {}, removeListener: () => {},
      addEventListener: () => {}, removeEventListener: () => {}, dispatchEvent: () => false,
    }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

const TODAY = toDateOnly(new Date());

function setPermissions(granted: string[]) {
  useAuthStore.setState({
    hasPermission: (p: string) => granted.includes(p),
    isAccessSettingEnabled: () => true,
  });
}

const row: DailyAttendanceRow = {
  employeeId: "emp-1",
  employeeName: "Ayşe Yılmaz",
  orgUnitName: "Muhasebe",
  date: TODAY,
  checkIn: "09:05:00",
  checkOut: "18:00:00",
  workedMinutes: 475,
  overtimeMinutes: 0,
  anomalies: [],
  checkInPunchId: "punch-1",
  checkOutPunchId: "punch-2",
};

function paged(items: DailyAttendanceRow[]) {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1, hasNext: false, hasPrevious: false };
}

function LocationProbe() {
  const loc = useLocation();
  return <div data-testid="loc">{loc.pathname + loc.search}</div>;
}

function renderPage(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={i18next}>
      <QueryClientProvider client={client}>
        <MantineProvider>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route path="/app/attendance/daily/:status" element={<><DailyListPage /><LocationProbe /></>} />
            </Routes>
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

beforeEach(async () => {
  vi.clearAllMocks();
  if (!i18next.isInitialized) {
    await i18next.init({
      lng: "tr",
      fallbackLng: "tr",
      ns: ["attendance-daily", "common"],
      resources: { tr: { "attendance-daily": trDaily, common: trCommon } },
      interpolation: { escapeValue: false },
    });
  }
  setPermissions(["attendance.daily.view", "attendance.punch.manage"]);
  vi.mocked(listDaily).mockResolvedValue(paged([row]));
  vi.mocked(getDailyCounts).mockResolvedValue({ arrived: 12, late: 3, absent: 2 });
  vi.mocked(addManualPunch).mockResolvedValue("new-id");
  vi.mocked(deletePunch).mockResolvedValue();
});

describe("DailyListPage", () => {
  it("renders_status_from_route_param", async () => {
    renderPage("/app/attendance/daily/late");
    expect(await screen.findByText("Ayşe Yılmaz")).toBeInTheDocument();
    expect(listDaily).toHaveBeenCalledWith(TODAY, "late", undefined, 1);
    expect(screen.getByRole("tab", { selected: true })).toHaveTextContent(trDaily.dailyList.status.late);
  });

  it("date_navigation_updates_query", async () => {
    const user = userEvent.setup();
    renderPage("/app/attendance/daily/arrived");
    await screen.findByText("Ayşe Yılmaz");
    await user.click(screen.getByRole("button", { name: trDaily.dailyList.previousDay }));
    const prev = addDaysToDateOnly(TODAY, -1);
    await waitFor(() => expect(listDaily).toHaveBeenLastCalledWith(prev, "arrived", undefined, 1));
    expect(screen.getByTestId("loc")).toHaveTextContent(`date=${prev}`);
  });

  it("counts_badges_match_service", async () => {
    renderPage("/app/attendance/daily/arrived");
    const lateTab = await screen.findByRole("tab", { name: new RegExp(trDaily.dailyList.status.late) });
    await waitFor(() => expect(within(lateTab).getByText("3")).toBeInTheDocument());
    expect(within(screen.getByRole("tab", { name: new RegExp(trDaily.dailyList.status.arrived) })).getByText("12")).toBeInTheDocument();
    expect(getDailyCounts).toHaveBeenCalledWith(TODAY, undefined);
  });

  it("manual_punch_requires_reason", async () => {
    const user = userEvent.setup();
    renderPage("/app/attendance/daily/arrived");
    await screen.findByText("Ayşe Yılmaz");
    await user.click(screen.getByRole("button", { name: trDaily.dailyList.addPunch }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: trDaily.dailyList.save }));
    expect(addManualPunch).not.toHaveBeenCalled();
    expect(within(dialog).getByText(trDaily.dailyList.reasonRequired)).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText(new RegExp(trDaily.dailyList.reason)), "Terminal arızası");
    await user.click(within(dialog).getByRole("button", { name: trDaily.dailyList.save }));
    await waitFor(() => expect(addManualPunch).toHaveBeenCalledTimes(1));
    expect(vi.mocked(addManualPunch).mock.calls[0][0]).toMatchObject({ employeeId: "emp-1", reason: "Terminal arızası" });
  });

  it("delete_punch_confirms_and_sends_reason", async () => {
    const user = userEvent.setup();
    renderPage("/app/attendance/daily/arrived");
    await screen.findByText("Ayşe Yılmaz");
    await user.click(screen.getByRole("button", { name: trDaily.dailyList.deleteCheckIn }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: trDaily.dailyList.confirmDelete }));
    expect(deletePunch).not.toHaveBeenCalled();
    await user.type(within(dialog).getByLabelText(new RegExp(trDaily.dailyList.reason)), "Yanlış okutma");
    await user.click(within(dialog).getByRole("button", { name: trDaily.dailyList.confirmDelete }));
    await waitFor(() => expect(deletePunch).toHaveBeenCalledWith("punch-1", "Yanlış okutma"));
  });

  it("locked_month_error_shown", async () => {
    const user = userEvent.setup();
    vi.mocked(deletePunch).mockRejectedValue(
      new AxiosError("locked", "ERR_BAD_REQUEST", undefined, undefined, {
        status: 409, statusText: "Conflict", headers: {}, config: {} as never,
        data: { detail: "Bu ay kilitli." },
      }),
    );
    renderPage("/app/attendance/daily/arrived");
    await screen.findByText("Ayşe Yılmaz");
    await user.click(screen.getByRole("button", { name: trDaily.dailyList.deleteCheckIn }));
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText(new RegExp(trDaily.dailyList.reason)), "x");
    await user.click(within(dialog).getByRole("button", { name: trDaily.dailyList.confirmDelete }));
    expect(await within(dialog).findByText("Bu ay kilitli.")).toBeInTheDocument();
  });
});
