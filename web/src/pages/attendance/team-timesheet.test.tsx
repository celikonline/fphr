import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAttendance from "../../../public/locales/tr/attendance.json";
import trCommon from "../../../public/locales/tr/common.json";
import trEmployees from "../../../public/locales/tr/employees.json";
import TeamTimesheetPage from "./team-timesheet";
import { getTeamTimesheet, type TeamWeekRow } from "@/services/attendance.service";
import { toDateOnly } from "@/lib/dates";

vi.mock("@/services/attendance.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/attendance.service")>(
    "@/services/attendance.service"
  );
  return { ...actual, getTeamTimesheet: vi.fn(), getTeamMonth: vi.fn() };
});
vi.mock("@/components/permission-guard", () => ({ usePermission: () => true }));
vi.mock("@/components/employees/org-unit-select", () => ({ default: () => null }));
vi.mock("@/components/leave/leave-shared", () => ({ useLeaveName: () => () => "" }));

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
  } as unknown as typeof ResizeObserver;
});

afterEach(() => vi.clearAllMocks());

function rowsFixture(): TeamWeekRow[] {
  const base = { breakMinutesApplied: 0, nightMinutes: 0, dailyLimitExceeded: false, nightLimitExceeded: false };
  const today = toDateOnly(new Date());
  return [
    {
      employeeId: "emp-1",
      employeeName: "Ayşe Yılmaz",
      days: [
        { ...base, date: today, workedMinutes: 0, status: "unresolved", anomalies: [], incalculableReason: "brokenSequence" },
      ],
    },
  ];
}

function renderPage() {
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
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider env="test">
          <TeamTimesheetPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("TeamTimesheetPage incalculableReason", () => {
  it("shows the localized reason hint on a cell whose day is incalculable", async () => {
    vi.mocked(getTeamTimesheet).mockResolvedValue(rowsFixture());
    renderPage();
    await screen.findAllByText("Ayşe Yılmaz");

    const hints = await screen.findAllByLabelText(/Hesaplanamaz: /);
    expect(hints[0].getAttribute("aria-label")).toContain(trAttendance.incalculableReason.brokenSequence);
  });
});
