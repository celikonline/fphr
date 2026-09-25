import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAttendance from "../../../public/locales/tr/attendance.json";
import trCommon from "../../../public/locales/tr/common.json";
import trEmployees from "../../../public/locales/tr/employees.json";
import ShiftPlanPage from "./shift-plan";
import { useAuthStore } from "@/store/auth.store";
import { listEmployees } from "@/services/employees.service";
import {
  getShiftPlanLockStatus,
  getUnpublishedShiftCount,
  listShiftAssignments,
} from "@/services/attendance.service";
import { listShiftDefinitions } from "@/services/attendance-shift-definitions.service";
import {
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


function setPermissions(granted: string[]) {
  useAuthStore.setState({
    hasPermission: (permission: string) => granted.includes(permission),
    isAccessSettingEnabled: () => true,
  });
}

function renderPage() {
  const i18n = i18next.createInstance();
  void i18n.init({
    lng: "tr",
    defaultNS: "attendance",
    ns: ["attendance", "common", "employees"],
    resources: { tr: { attendance: trAttendance, common: trCommon, employees: trEmployees } },
    react: { useSuspense: false },
  });
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
        <MantineProvider env="test">
          <ShiftPlanPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

beforeEach(() => {
  vi.mocked(listEmployees).mockResolvedValue({ items: [{ id: "e1", displayName: "Ayşe Yılmaz" }], total: 1 } as never);
  vi.mocked(listShiftAssignments).mockResolvedValue([]);
  vi.mocked(getUnpublishedShiftCount).mockResolvedValue(0);
  vi.mocked(getShiftPlanLockStatus).mockResolvedValue({ isLocked: false } as never);
  vi.mocked(listShiftDefinitions).mockResolvedValue([]);
  vi.mocked(getShiftPlanTotals).mockResolvedValue({ byDate: [], byEmployee: [] });
  vi.mocked(listUnassignedEmployeeIds).mockResolvedValue([]);
});

describe("ShiftPlanPage şablon düğmesi yetkisi (B10: attendance.shifttemplate.manage)", () => {
  it("yalnız plan izni olan kullanıcı şablon düğmesini görmez", async () => {
    setPermissions(["attendance.shift.view", "attendance.shift.plan"]);
    renderPage();
    await screen.findAllByText("Ayşe Yılmaz");
    expect(screen.queryByRole("button", { name: "Plan şablonları" })).not.toBeInTheDocument();
  });

  it("shifttemplate.manage izni olan kullanıcı düğmeyi görür", async () => {
    setPermissions(["attendance.shift.view", "attendance.shifttemplate.manage"]);
    renderPage();
    await screen.findAllByText("Ayşe Yılmaz");
    expect(screen.getByRole("button", { name: "Plan şablonları" })).toBeInTheDocument();
  });

  it("plan + manage birlikte düğmeyi gösterir; hiçbiri yoksa göstermez", async () => {
    setPermissions(["attendance.shift.view", "attendance.shift.plan", "attendance.shifttemplate.manage"]);
    const { unmount } = renderPage();
    await screen.findAllByText("Ayşe Yılmaz");
    expect(screen.getByRole("button", { name: "Plan şablonları" })).toBeInTheDocument();
    unmount();
    setPermissions(["attendance.shift.view"]);
    renderPage();
    await screen.findAllByText("Ayşe Yılmaz");
    expect(screen.queryByRole("button", { name: "Plan şablonları" })).not.toBeInTheDocument();
  });
});
