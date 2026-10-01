import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trHome from "../../public/locales/tr/home.json";
import trNavigation from "../../public/locales/tr/navigation.json";
import trCommon from "../../public/locales/tr/common.json";
import { useAuthStore } from "@/store/auth.store";
import HomePage from "./home";
import { getLeaveCalendar, type LeaveCalendarEntry } from "@/services/leave.service";
import { getUpcomingHolidays } from "@/services/home.service";
import { listInbox } from "@/services/workflow.service";
import { addDaysToDateOnly, toDateOnly } from "@/lib/dates";

// senseik-042 (halka grafik merkezi): the donut's center must show the grand total headcount,
// never a department name - the previous behavior showed the largest department's name/count.
vi.mock("@/services/leave.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/leave.service")>(
    "@/services/leave.service"
  );
  return { ...actual, getLeaveCalendar: vi.fn() };
});

vi.mock("@/services/home.service", async () => {
  const actual =
    await vi.importActual<typeof import("@/services/home.service")>("@/services/home.service");
  return { ...actual, getUpcomingHolidays: vi.fn() };
});

vi.mock("@/services/workflow.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/workflow.service")>("@/services/workflow.service");
  return { ...actual, listInbox: vi.fn() };
});

vi.mock("@/services/employees.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/employees.service")>(
    "@/services/employees.service"
  );
  return {
    ...actual,
    getDepartmentDistribution: vi.fn().mockResolvedValue([
      { orgUnitId: "1", departmentName: "Mühendislik", count: 7 },
      { orgUnitId: "2", departmentName: "Satış", count: 3 },
    ]),
  };
});

// jsdom has neither matchMedia nor ResizeObserver; Mantine/recharts need both to render without throwing.
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

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "home",
    ns: ["home", "navigation", "common"],
    resources: { tr: { home: trHome, navigation: trNavigation, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

/** Only `employee.directory.view` is granted, and no access-setting feature is enabled, so of every
 * gated widget on the dashboard only "Çalışan Dağılımı" (department distribution) mounts - keeping
 * this test from having to mock every other home widget's service call. */
function signInWithPermissions(permissions: string[]) {
  useAuthStore.setState({
    hasHydrated: true,
    token: "test-token",
    user: {
      id: "u1",
      email: "user@example.com",
      name: "Test User",
      roles: [],
      roleLabels: [],
      permissions,
    },
    features: {
      access: {} as never,
      accessSettingsBypass: false,
      mfaSetupRequired: false,
      disabledModules: [],
    },
  });
}

function signInWithDirectoryPermissionOnly() {
  signInWithPermissions(["employee.directory.view"]);
}

function renderHome() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter initialEntries={["/app"]}>
            <HomePage />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("HomePage department distribution donut (senseik-042)", () => {
  it("shows an empty approval state when inbox is the only workflow widget", async () => {
    vi.mocked(listInbox).mockResolvedValue([]);
    signInWithPermissions(["workflow.inbox.view"]);
    renderHome();

    expect(await screen.findByText("Onayınızı bekleyen talep yok.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Ana sayfa", level: 1 })).toBeInTheDocument();
    expect(screen.queryByText("Bugünün odağı")).not.toBeInTheDocument();
    expect(screen.queryByText("Gündeminiz")).not.toBeInTheDocument();
    expect(screen.queryByText("Çevrenizde olanlar")).not.toBeInTheDocument();
    expect(screen.getByText("Onay Bekleyenler")).toBeInTheDocument();
  });

  it("does not show the removed focus panel to employee-only roles", () => {
    signInWithPermissions(["employee.employees.read"]);
    renderHome();
    expect(screen.getByRole("heading", { name: "Ana sayfa", level: 1 })).toBeInTheDocument();
    expect(screen.queryByText("Bugünün odağı")).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Çalışanları gör" })).not.toBeInTheDocument();
  });

  it("keeps unauthorized workflow widgets and their empty section out of the new layout", async () => {
    signInWithDirectoryPermissionOnly();
    renderHome();

    expect(screen.getByRole("heading", { name: "Ana sayfa", level: 1 })).toBeInTheDocument();
    expect(screen.queryByText("Bugünün odağı")).not.toBeInTheDocument();
    expect(screen.queryByText("Gündeminiz")).not.toBeInTheDocument();
    expect(screen.queryByText("Çevrenizde olanlar")).not.toBeInTheDocument();
    expect(screen.queryByText("Onay Bekleyenler")).not.toBeInTheDocument();
  });

  it("shows the total headcount at the donut's center, not the department name", async () => {
    signInWithDirectoryPermissionOnly();
    renderHome();

    const widget = await screen.findByText("Çalışan Dağılımı");
    const card = widget.closest(".mantine-Card-root") as HTMLElement;
    expect(card).not.toBeNull();

    // Total (7 + 3 = 10) must appear in the chart's center label, labeled "Toplam".
    const centerTotal = await screen.findByTestId("department-distribution-center-total");
    await waitFor(() => expect(centerTotal).toHaveTextContent("10"));
    const center = screen.getByTestId("department-distribution-center");
    expect(within(center).getByText("Toplam")).toBeInTheDocument();

    // Neither department name is shown as the center label (the pre-fix behavior) - they only
    // appear in the legend below the donut, outside the center block.
    expect(within(center).queryByText("Mühendislik")).toBeNull();
    expect(within(center).queryByText("Satış")).toBeNull();
  });

  it("lists each department's name and headcount in the legend below the donut", async () => {
    signInWithDirectoryPermissionOnly();
    renderHome();

    const widget = await screen.findByText("Çalışan Dağılımı");
    const card = widget.closest(".mantine-Card-root") as HTMLElement;

    await waitFor(() =>
      expect(screen.getByTestId("department-distribution-center-total")).toHaveTextContent("10")
    );

    expect(within(card).getByText("Mühendislik")).toBeInTheDocument();
    expect(within(card).getByText("7")).toBeInTheDocument();
    expect(within(card).getByText("Satış")).toBeInTheDocument();
    expect(within(card).getByText("3")).toBeInTheDocument();
  });
});

// D-14: "Bugün" must mean "starts today"; a leave that began earlier is under way, a later one shows its start date.
describe("HomePage upcoming leaves widget (D-14)", () => {
  function leaveEntry(
    requestId: string,
    employeeName: string,
    startDate: string
  ): LeaveCalendarEntry {
    return {
      requestId,
      employeeId: requestId,
      employeeName,
      startDate,
      endDate: addDaysToDateOnly(startDate, 5),
      calculatedDays: 1,
      status: "approved",
      isMasked: false,
    };
  }

  it("labels only leaves starting today as Bugün and already-started ones as ongoing", async () => {
    const today = toDateOnly(new Date());
    vi.mocked(getLeaveCalendar).mockResolvedValue([
      leaveEntry("r1", "Ayşe Bugün", today),
      leaveEntry("r2", "Mehmet Devam", addDaysToDateOnly(today, -2)),
      leaveEntry("r3", "Zeynep Sonra", addDaysToDateOnly(today, 7)),
    ]);
    useAuthStore.setState({
      hasHydrated: true,
      token: "test-token",
      user: {
        id: "u1",
        email: "user@example.com",
        name: "Test User",
        roles: [],
        roleLabels: [],
        permissions: ["leave.my.calendar"],
      },
      features: {
        access: {} as never,
        accessSettingsBypass: true,
        mfaSetupRequired: false,
        disabledModules: [],
      },
    });
    renderHome();

    const widget = await screen.findByText("Yaklaşan İzinler");
    const card = widget.closest(".mantine-Card-root") as HTMLElement;

    expect(await within(card).findByText("Bugün")).toBeInTheDocument();
    expect(within(card).getByText("Devam ediyor")).toBeInTheDocument();
    expect(within(card).queryAllByText("Bugün")).toHaveLength(1);
    expect(within(card).getByText("Zeynep Sonra")).toBeInTheDocument();
  });
});

// Arife (half day) holidays show a "0,5g" count instead of the full-day "1g" the calendar-day run would imply.
describe("HomePage holidays widget (arife half-day)", () => {
  it("shows 0,5g for a half-day arife, the run length for a multi-day bayram, and 1g for a plain single day", async () => {
    const today = toDateOnly(new Date());
    vi.mocked(getUpcomingHolidays).mockResolvedValue([
      {
        date: addDaysToDateOnly(today, 10),
        name: "Bayram Arifesi",
        holidayCalendarId: "c1",
        calendarName: "Genel",
        isHalfDay: true,
      },
      {
        date: addDaysToDateOnly(today, 11),
        name: "Bayram",
        holidayCalendarId: "c1",
        calendarName: "Genel",
        isHalfDay: false,
      },
      {
        date: addDaysToDateOnly(today, 12),
        name: "Bayram",
        holidayCalendarId: "c1",
        calendarName: "Genel",
        isHalfDay: false,
      },
      {
        date: addDaysToDateOnly(today, 13),
        name: "Bayram",
        holidayCalendarId: "c1",
        calendarName: "Genel",
        isHalfDay: false,
      },
      // No `isHalfDay` at all (older API response shape) - must be treated as a full day.
      {
        date: addDaysToDateOnly(today, 20),
        name: "Yılbaşı",
        holidayCalendarId: "c1",
        calendarName: "Genel",
      },
    ]);
    useAuthStore.setState({
      hasHydrated: true,
      token: "test-token",
      user: {
        id: "u1",
        email: "user@example.com",
        name: "Test User",
        roles: [],
        roleLabels: [],
        permissions: ["organization.holidays.view"],
      },
      features: {
        access: {} as never,
        accessSettingsBypass: true,
        mfaSetupRequired: false,
        disabledModules: [],
      },
    });
    renderHome();

    const widget = await screen.findByText("Resmi Tatiller");
    const card = widget.closest(".mantine-Card-root") as HTMLElement;

    expect(await within(card).findByText("0,5g")).toBeInTheDocument();
    expect(within(card).getByText("3g")).toBeInTheDocument();
    expect(within(card).getByText("1g")).toBeInTheDocument();
  });
});
