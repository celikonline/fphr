import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trLeave from "../../../public/locales/tr/leave.json";
import trCommon from "../../../public/locales/tr/common.json";
import trEmployees from "../../../public/locales/tr/employees.json";
import LeaveCalendarPage from "./leave-calendar";
import calendarClasses from "./leave-calendar.module.css";
import { useAuthStore } from "@/store/auth.store";
import { toDateOnly } from "@/lib/dates";
import { getLeaveCalendar, type LeaveCalendarEntry } from "@/services/leave.service";

// senseik-024: month view already worked (custom CSS Grid + a "lane" algorithm for same-employee overlaps).
// These tests cover the new Outlook-style day/week/month view switch: the view-scoped fetch range, that every
// view renders the fetched data, and that the lane algorithm still separates overlapping entries in week/month.
vi.mock("@/services/leave.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/leave.service")>("@/services/leave.service");
  return { ...actual, getLeaveCalendar: vi.fn() };
});

// The org-unit filter is unrelated to view switching; stub it out so the test does not depend on a network call.
vi.mock("@/components/employees/use-org-units", () => ({
  useOrgUnits: () => ({ options: [], nameById: new Map(), isLoading: false }),
}));

// jsdom has no matchMedia/ResizeObserver; Mantine's Tooltip/SegmentedControl need at least a stub of each.
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

// The real "now" (no fake timers: they hang RTL's findBy/waitFor polling). Fixture entries are dated on it so
// they fall inside the month/week/day ranges regardless of which day the test happens to run on.
const TODAY = new Date();
const TODAY_ISO = toDateOnly(TODAY);

/** Same month-boundary math as the component, used only to compute the *expected* fetch range for assertions. */
function monthRange(date: Date): { from: string; to: string } {
  return {
    from: toDateOnly(new Date(date.getFullYear(), date.getMonth(), 1)),
    to: toDateOnly(new Date(date.getFullYear(), date.getMonth() + 1, 0)),
  };
}

beforeEach(() => {
  useAuthStore.setState({ hasPermission: () => true, isAccessSettingEnabled: () => true });
});

afterEach(() => {
  vi.mocked(getLeaveCalendar).mockReset();
});

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "leave",
    ns: ["leave", "common", "employees"],
    resources: { tr: { leave: trLeave, common: trCommon, employees: trEmployees } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

/** Two overlapping (same day, same employee) approved/pending entries, plus one entry for a second employee. */
function overlappingFixture(): LeaveCalendarEntry[] {
  return [
    {
      requestId: "req-1",
      employeeId: "emp-1",
      employeeName: "Ayşe Yılmaz",
      startDate: TODAY_ISO,
      endDate: TODAY_ISO,
      calculatedDays: 1,
      status: "approved",
      isMasked: false,
      leaveTypeName: "Yıllık İzin",
    },
    {
      requestId: "req-2",
      employeeId: "emp-1",
      employeeName: "Ayşe Yılmaz",
      startDate: TODAY_ISO,
      endDate: TODAY_ISO,
      calculatedDays: 1,
      status: "pending",
      isMasked: false,
      leaveTypeName: "Mazeret İzni",
    },
    {
      requestId: "req-3",
      employeeId: "emp-2",
      employeeName: "Baran Demir",
      startDate: TODAY_ISO,
      endDate: TODAY_ISO,
      calculatedDays: 1,
      status: "approved",
      isMasked: false,
      leaveTypeName: "Doğum İzni",
    },
  ];
}

/** Mimics the API: only entries overlapping the requested [from, to] range come back. */
function mockCalendarData(entries: LeaveCalendarEntry[]) {
  vi.mocked(getLeaveCalendar).mockImplementation(async ({ from, to }) => entries.filter((e) => e.startDate <= to && e.endDate >= from));
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const { container } = render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <LeaveCalendarPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
  return container;
}

function daysBetween(fromIso: string, toIso: string): number {
  return Math.round((new Date(toIso).getTime() - new Date(fromIso).getTime()) / 86_400_000);
}

/**
 * Desktop grid bars only (`.bar`), distinct from the "narrow screens" day-list rendering of the same entries —
 * jsdom does not evaluate the `hidden md:block` / `md:hidden` media-query classes, so both are present in the DOM
 * at once. Scoping through the CSS module class is the only unambiguous way to reach grid-specific bars in a test.
 */
function gridBars(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(`.${calendarClasses.bar}`));
}

function lastCall() {
  const calls = vi.mocked(getLeaveCalendar).mock.calls;
  const call = calls[calls.length - 1]?.[0];
  if (!call) throw new Error("getLeaveCalendar was not called");
  return call;
}

describe("LeaveCalendar (senseik-024 Outlook view switch)", () => {
  it("switches month -> week -> day, narrowing the fetch range and re-rendering the fetched data each time", async () => {
    mockCalendarData(overlappingFixture());
    const user = userEvent.setup({ delay: null });
    const container = renderPage();

    // Month (default): full calendar month, existing grid still shows both employees.
    await screen.findByText("Çalışan");
    expect(gridBars(container).length).toBeGreaterThan(0);
    let query = lastCall();
    const expectedMonth = monthRange(TODAY);
    expect(query.from).toBe(expectedMonth.from);
    expect(query.to).toBe(expectedMonth.to);
    expect(screen.getByRole("radio", { name: "Ay" })).toBeChecked();

    // Week: fetch narrows to 7 days, grid still renders the same fixture.
    await user.click(screen.getByRole("radio", { name: "Hafta" }));
    await screen.findByText("Çalışan");
    expect(gridBars(container).length).toBeGreaterThan(0);
    query = lastCall();
    expect(daysBetween(query.from, query.to)).toBe(6);

    // Day: fetch narrows to a single day, grid is replaced by the per-employee list (requirement 4).
    await user.click(screen.getByRole("radio", { name: "Gün" }));
    await screen.findByText("Baran Demir");
    expect(gridBars(container).length).toBe(0);
    query = lastCall();
    expect(query.from).toBe(query.to);
    expect(query.from).toBe(TODAY_ISO);
  });

  it("day view lists every employee's leave for the day row by row", async () => {
    mockCalendarData(overlappingFixture());
    const user = userEvent.setup({ delay: null });
    renderPage();
    await screen.findByText("Çalışan");

    await user.click(screen.getByRole("radio", { name: "Gün" }));

    // Both of Ayşe's overlapping entries and Baran's entry each appear once, as separate rows (row per leave request).
    expect(await screen.findByText(/Yıllık İzin/)).toBeInTheDocument();
    expect(screen.getByText(/Mazeret İzni/)).toBeInTheDocument();
    expect(screen.getByText(/Doğum İzni/)).toBeInTheDocument();
    // Ayşe has two overlapping requests that day, so her name shows once per row.
    expect(screen.getAllByText("Ayşe Yılmaz")).toHaveLength(2);
    expect(screen.getAllByText("Baran Demir")).toHaveLength(1);
  });

  it("keeps overlapping same-employee entries in separate lanes in month view (existing behavior preserved)", async () => {
    mockCalendarData(overlappingFixture());
    const container = renderPage();
    await screen.findByText("Çalışan");

    const bars = gridBars(container);
    expect(bars).toHaveLength(3);
    const approved = bars.find((bar) => bar.textContent?.includes("Yıllık İzin"));
    const pending = bars.find((bar) => bar.textContent?.includes("Mazeret İzni"));
    expect(approved).toBeTruthy();
    expect(pending).toBeTruthy();
    expect(approved!.style.gridRow).not.toBe(pending!.style.gridRow);
  });

  it("keeps overlapping same-employee entries in separate lanes in week view (lane algorithm adapted)", async () => {
    mockCalendarData(overlappingFixture());
    const user = userEvent.setup({ delay: null });
    const container = renderPage();
    await screen.findByText("Çalışan");

    await user.click(screen.getByRole("radio", { name: "Hafta" }));
    await screen.findByText("Çalışan");

    const bars = gridBars(container);
    expect(bars).toHaveLength(3);
    const approved = bars.find((bar) => bar.textContent?.includes("Yıllık İzin"));
    const pending = bars.find((bar) => bar.textContent?.includes("Mazeret İzni"));
    expect(approved).toBeTruthy();
    expect(pending).toBeTruthy();
    expect(approved!.style.gridRow).not.toBe(pending!.style.gridRow);
  });
});
