import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trLeave from "../../../public/locales/tr/leave.json";
import trCommon from "../../../public/locales/tr/common.json";
import { LeaveTypesTable } from "./leave-rule-sets";
import { setLeaveTypeDisabled, type LeaveRuleSet, type LeaveType } from "@/services/leave.service";

vi.mock("@/services/leave.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/leave.service")>("@/services/leave.service");
  return { ...actual, setLeaveTypeDisabled: vi.fn() };
});

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
  Object.defineProperty(document, "fonts", {
    writable: true,
    value: { addEventListener: () => {}, removeEventListener: () => {} },
  });
});

beforeEach(() => {
  vi.mocked(setLeaveTypeDisabled).mockReset();
  vi.mocked(setLeaveTypeDisabled).mockResolvedValue(undefined as never);
});

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "leave",
    ns: ["leave", "common"],
    resources: { tr: { leave: trLeave, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function leaveType(id: string, name: string, isDisabled: boolean): LeaveType {
  return {
    id,
    sortOrder: 1,
    settings: {
      code: "annual",
      name,
      isPaid: true,
      limitType: "unlimited",
      accrualFrequency: "none",
      carryOverMode: "none",
      unit: "fullDay",
      isDisabled,
    },
  } as unknown as LeaveType;
}

function renderTable(types: LeaveType[]) {
  const set: LeaveRuleSet = {
    id: "set-1",
    name: "Varsayılan",
    isDefault: true,
    isSystemSeed: false,
    unpaidLeaveShiftsAccrual: false,
    assignedEmployeeCount: 12,
    types,
  };
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <LeaveTypesTable set={set} canManageTypes onEdit={vi.fn()} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("LeaveTypesTable disable/enable (B9)", () => {
  it("asks for confirmation with the affected employee count before disabling", async () => {
    renderTable([leaveType("type-1", "Yıllık izin", false)]);

    fireEvent.click(screen.getByRole("button", { name: trLeave.ruleSets.disable }));

    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(trLeave.ruleSets.disableTitle)).not.toBeNull();
    expect(within(dialog).getByText(/12 çalışan/)).not.toBeNull();
    expect(setLeaveTypeDisabled).not.toHaveBeenCalled();

    fireEvent.click(within(dialog).getByRole("button", { name: trLeave.ruleSets.disable }));

    await waitFor(() => expect(setLeaveTypeDisabled).toHaveBeenCalledWith("set-1", "type-1", true));
  });

  it("does not disable when the confirmation is cancelled", async () => {
    renderTable([leaveType("type-1", "Yıllık izin", false)]);

    fireEvent.click(screen.getByRole("button", { name: trLeave.ruleSets.disable }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: trCommon.cancel }));

    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(setLeaveTypeDisabled).not.toHaveBeenCalled();
  });

  it("enables a disabled type immediately, without confirmation", async () => {
    renderTable([leaveType("type-2", "Mazeret izni", true)]);

    fireEvent.click(screen.getByRole("button", { name: trLeave.ruleSets.enable }));

    await waitFor(() => expect(setLeaveTypeDisabled).toHaveBeenCalledWith("set-1", "type-2", false));
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});
