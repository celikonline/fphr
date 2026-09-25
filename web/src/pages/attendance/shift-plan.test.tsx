import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAttendance from "../../../public/locales/tr/attendance.json";
import trCommon from "../../../public/locales/tr/common.json";
import trEmployees from "../../../public/locales/tr/employees.json";
import ShiftPlanPage from "./shift-plan";
import { SHIFT_PALETTE_MIME } from "@/components/attendance/shift-palette";
import { useAuthStore } from "@/store/auth.store";
import { toDateOnly } from "@/lib/dates";
import { listEmployees } from "@/services/employees.service";
import {
  getShiftPlanLockStatus,
  getUnpublishedShiftCount,
  listShiftAssignments,
  type ShiftAssignment,
} from "@/services/attendance.service";
import { listShiftDefinitions } from "@/services/attendance-shift-definitions.service";
import {
  bulkAssignShifts,
  bulkClearShifts,
  getShiftPlanTotals,
  listUnassignedEmployeeIds,
} from "@/services/attendance-shift-calendar.service";

vi.mock("@/services/employees.service", () => ({ listEmployees: vi.fn() }));
vi.mock("@/services/attendance.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/attendance.service")>("@/services/attendance.service");
  return {
    ...actual,
    listShiftAssignments: vi.fn(),
    getUnpublishedShiftCount: vi.fn(),
    getShiftPlanLockStatus: vi.fn(),
  };
});
vi.mock("@/services/attendance-shift-definitions.service", () => ({ listShiftDefinitions: vi.fn() }));
vi.mock("@/services/attendance-shift-calendar.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/attendance-shift-calendar.service")>(
    "@/services/attendance-shift-calendar.service"
  );
  return {
    ...actual,
    bulkAssignShifts: vi.fn(),
    bulkClearShifts: vi.fn(),
    listUnassignedEmployeeIds: vi.fn(),
    getShiftPlanTotals: vi.fn(),
  };
});
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));
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

function mondayOfToday(): Date {
  const now = new Date();
  const day = now.getDay();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate() + (day === 0 ? -6 : 1 - day));
}
const MONDAY = mondayOfToday();
const weekDay = (i: number) => toDateOnly(new Date(MONDAY.getFullYear(), MONDAY.getMonth(), MONDAY.getDate() + i));

function setPermissions(granted: string[]) {
  useAuthStore.setState({
    hasPermission: (permission: string) => granted.includes(permission),
    isAccessSettingEnabled: () => true,
  });
}

function assignment(over: Partial<ShiftAssignment> & Record<string, unknown>): ShiftAssignment {
  return {
    id: "a1",
    employeeId: "e1",
    date: weekDay(0),
    start: "08:00:00",
    end: "16:00:00",
    breakMinutes: 0,
    isNight: false,
    status: "published",
    shiftMinutes: 480,
    ...over,
  } as ShiftAssignment;
}

function dataTransfer() {
  const store = new Map<string, string>();
  return {
    setData: (k: string, v: string) => void store.set(k, v),
    getData: (k: string) => store.get(k) ?? "",
    effectAllowed: "",
    dropEffect: "",
    types: [],
  };
}

const cell = (employeeId: string, index: number) => screen.getByTestId(`shift-cell-${employeeId}-${weekDay(index)}`);

beforeEach(() => {
  setPermissions(["attendance.shift.view", "attendance.shift.plan", "attendance.shift.publish", "attendance.shift.lock"]);
  vi.mocked(listEmployees).mockResolvedValue({
    items: [
      { id: "e1", displayName: "Ayşe Yılmaz" },
      { id: "e2", displayName: "Baran Demir" },
    ],
    total: 2,
  } as never);
  vi.mocked(listShiftAssignments).mockResolvedValue([]);
  vi.mocked(getUnpublishedShiftCount).mockResolvedValue(0);
  vi.mocked(getShiftPlanLockStatus).mockResolvedValue({ isLocked: false } as never);
  vi.mocked(listShiftDefinitions).mockResolvedValue([
    {
      id: "sd1",
      code: "S",
      name: "Sabah",
      colorHex: "#22aa55",
      type: "fixed",
      startMinute: 480,
      endMinute: 960,
      startsPreviousDay: false,
      breaks: [],
      lateToleranceMinutes: 0,
      earlyLeaveToleranceMinutes: 0,
      bonusMinutes: 0,
      isActive: true,
    },
  ]);
  vi.mocked(getShiftPlanTotals).mockResolvedValue({ byDate: [], byEmployee: [] });
  vi.mocked(bulkAssignShifts).mockResolvedValue({ affectedCount: 1, skipped: [] });
  vi.mocked(bulkClearShifts).mockResolvedValue({ affectedCount: 1, skipped: [] });
  vi.mocked(listUnassignedEmployeeIds).mockResolvedValue([]);
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
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider env="test">
          <ShiftPlanPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

async function findPalette(name: string) {
  return screen.findByTestId(`palette-${name}`);
}

describe("ShiftPlanPage calendar (Z3.1)", () => {
  it("drag_palette_shift_to_cell_assigns", async () => {
    renderPage();
    const chip = await findPalette("shift-sd1");
    expect(chip).toHaveTextContent("Sabah 08:00–16:00");
    const dt = dataTransfer();
    fireEvent.dragStart(chip, { dataTransfer: dt });
    const target = await screen.findByTestId(`shift-cell-e1-${weekDay(2)}`);
    fireEvent.dragOver(target, { dataTransfer: dt });
    fireEvent.drop(target, { dataTransfer: dt });

    await waitFor(() =>
      expect(bulkAssignShifts).toHaveBeenCalledWith({
        employeeIds: ["e1"],
        dates: [weekDay(2)],
        shiftDefinitionId: "sd1",
        dayType: "work",
      })
    );
  });

  it("day_type_cell_clears_hours", async () => {
    vi.mocked(listShiftAssignments).mockResolvedValue([
      assignment({ id: "a2", date: weekDay(1), dayType: "weeklyRest", start: "00:00:00", end: "00:00:00", shiftMinutes: 0 }),
    ]);
    renderPage();
    const restCell = await screen.findByTestId(`shift-cell-e1-${weekDay(1)}`);
    expect(within(restCell).getByText("Haftalık izin")).toBeInTheDocument();
    expect(within(restCell).queryByText(/00:00/)).not.toBeInTheDocument();

    const chip = await findPalette("daytype-restDay");
    const dt = dataTransfer();
    fireEvent.dragStart(chip, { dataTransfer: dt });
    fireEvent.drop(await screen.findByTestId(`shift-cell-e2-${weekDay(3)}`), { dataTransfer: dt });
    await waitFor(() =>
      expect(bulkAssignShifts).toHaveBeenCalledWith({ employeeIds: ["e2"], dates: [weekDay(3)], dayType: "restDay" })
    );
  });

  it("bulk_select_range_and_assign", async () => {
    const user = userEvent.setup();
    renderPage();
    fireEvent.click(await screen.findByTestId(`shift-cell-e1-${weekDay(0)}`));
    fireEvent.click(cell("e2", 2), { shiftKey: true });
    expect(await screen.findByText("6 hücre seçili")).toBeInTheDocument();

    await user.click(await findPalette("shift-sd1"));
    await waitFor(() =>
      expect(bulkAssignShifts).toHaveBeenCalledWith({
        employeeIds: ["e1", "e2"],
        dates: [weekDay(0), weekDay(1), weekDay(2)],
        shiftDefinitionId: "sd1",
        dayType: "work",
      })
    );

    fireEvent.click(cell("e1", 0));
    await user.click(await screen.findByRole("button", { name: "Seçili vardiyaları sil" }));
    await user.click(await screen.findByRole("button", { name: "Sil" }));
    await waitFor(() =>
      expect(bulkClearShifts).toHaveBeenCalledWith({ employeeIds: ["e1"], dates: [weekDay(0)] })
    );
  });

  it("unassigned_filter_lists_employees", async () => {
    const user = userEvent.setup();
    vi.mocked(listUnassignedEmployeeIds).mockResolvedValue(["e2"]);
    renderPage();
    await screen.findByTestId(`shift-cell-e1-${weekDay(0)}`);
    await user.click(screen.getByRole("switch", { name: "Vardiyasız çalışanlar" }));

    await waitFor(() => expect(screen.queryByTestId(`shift-cell-e1-${weekDay(0)}`)).not.toBeInTheDocument());
    expect(screen.getByTestId(`shift-cell-e2-${weekDay(0)}`)).toBeInTheDocument();
    expect(listUnassignedEmployeeIds).toHaveBeenCalledWith({ from: weekDay(0), to: weekDay(6), employeeIds: ["e1", "e2"] });
  });

  it("footer_totals_sum_per_day", async () => {
    vi.mocked(getShiftPlanTotals).mockResolvedValue({
      byDate: [
        { date: weekDay(0), employeeCount: 2, totalMinutes: 960 },
        { date: weekDay(1), employeeCount: 1, totalMinutes: 450 },
      ],
      byEmployee: [],
    });
    renderPage();
    const monday = await screen.findByTestId(`shift-total-${weekDay(0)}`);
    await waitFor(() => expect(monday).toHaveTextContent("16 sa"));
    expect(monday).toHaveTextContent("2 kişi");
    expect(screen.getByTestId(`shift-total-${weekDay(1)}`)).toHaveTextContent("7 sa 30 dk");
    expect(screen.getByTestId(`shift-total-${weekDay(2)}`)).toHaveTextContent("—");
  });

  it("locked_period_disables_edit", async () => {
    vi.mocked(getShiftPlanLockStatus).mockResolvedValue({ isLocked: true, lockedAt: "2026-09-01T00:00:00Z" } as never);
    renderPage();
    const chip = await findPalette("shift-sd1");
    await waitFor(() => expect(chip).toHaveAttribute("aria-disabled", "true"));
    expect(chip).toHaveAttribute("draggable", "false");

    const dt = dataTransfer();
    dt.setData(SHIFT_PALETTE_MIME, JSON.stringify({ kind: "shift", shiftDefinitionId: "sd1" }));
    fireEvent.drop(await screen.findByTestId(`shift-cell-e1-${weekDay(0)}`), { dataTransfer: dt });
    fireEvent.click(cell("e1", 0));
    expect(bulkAssignShifts).not.toHaveBeenCalled();
    expect(screen.queryByText(/hücre seçili/)).not.toBeInTheDocument();
  });

  it("hides_palette_without_plan_permission", async () => {
    setPermissions(["attendance.shift.view"]);
    renderPage();
    await screen.findByTestId(`shift-cell-e1-${weekDay(0)}`);
    expect(screen.queryByTestId("palette-shift-sd1")).not.toBeInTheDocument();
  });

  it("keyboard_armed_palette_chip_then_enter_on_cell_assigns", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await findPalette("shift-sd1"));
    expect(bulkAssignShifts).not.toHaveBeenCalled();
    const target = await screen.findByTestId(`shift-cell-e2-${weekDay(1)}`);
    expect(target).toHaveAttribute("role", "gridcell");
    target.focus();
    await user.keyboard("{Enter}");
    await waitFor(() =>
      expect(bulkAssignShifts).toHaveBeenCalledWith({
        employeeIds: ["e2"],
        dates: [weekDay(1)],
        shiftDefinitionId: "sd1",
        dayType: "work",
      })
    );
  });

  it("keyboard_arrows_move_focus_and_shift_arrows_select_range", async () => {
    const user = userEvent.setup();
    renderPage();
    const start = await screen.findByTestId(`shift-cell-e1-${weekDay(0)}`);
    start.focus();
    await user.keyboard("{ArrowRight}");
    expect(cell("e1", 1)).toHaveFocus();
    await user.keyboard("{ArrowDown}");
    expect(cell("e2", 1)).toHaveFocus();
    await user.keyboard("{ArrowLeft}{ArrowUp}");
    expect(cell("e1", 0)).toHaveFocus();

    await user.keyboard("{Shift>}{ArrowRight}{ArrowRight}{ArrowDown}{/Shift}");
    expect(await screen.findByText("6 hücre seçili")).toBeInTheDocument();
    expect(cell("e2", 2)).toHaveFocus();
    expect(cell("e1", 0)).toHaveAttribute("aria-selected", "true");
    expect(cell("e2", 2)).toHaveAttribute("aria-selected", "true");
    await user.keyboard("{Shift>}{ArrowLeft}{/Shift}");
    expect(await screen.findByText("4 hücre seçili")).toBeInTheDocument();
  });

  it("locks_cells_per_month_when_week_spans_two_months", async () => {
    vi.useFakeTimers({ toFake: ["Date"], now: new Date(2026, 8, 30, 12) }); // Wed 30 Sep: week 28 Sep – 4 Oct
    try {
      vi.mocked(getShiftPlanLockStatus).mockImplementation(
        async (month: string) => ({ isLocked: month.startsWith("2026-10") }) as never
      );
      renderPage();
      const septCell = await screen.findByTestId("shift-cell-e1-2026-09-29");
      const octCell = await screen.findByTestId("shift-cell-e1-2026-10-01");
      await waitFor(() => expect(octCell).toHaveAttribute("aria-disabled", "true"));
      expect(septCell).not.toHaveAttribute("aria-disabled", "true");

      const dt = dataTransfer();
      dt.setData(SHIFT_PALETTE_MIME, JSON.stringify({ kind: "shift", shiftDefinitionId: "sd1" }));
      fireEvent.drop(octCell, { dataTransfer: dt });
      expect(bulkAssignShifts).not.toHaveBeenCalled();
      fireEvent.drop(septCell, { dataTransfer: dt });
      await waitFor(() => expect(bulkAssignShifts).toHaveBeenCalledTimes(1));
    } finally {
      vi.useRealTimers();
    }
  });
});
