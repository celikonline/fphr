import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trLeave from "../../../public/locales/tr/leave.json";
import trCommon from "../../../public/locales/tr/common.json";
import { LeaveTypeDialog } from "./leave-type-dialog";
import { updateLeaveType, type LeaveRuleSet, type LeaveType } from "@/services/leave.service";

// senseik-008 Bug 1 regression: the seed name is a raw resource key ("leave.type.annual") until the user edits it.
// LeaveTypeDialog must resolve it through useLeaveName() for display and on save — never show/send the raw key.
vi.mock("@/services/leave.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/leave.service")>("@/services/leave.service");
  return { ...actual, addLeaveType: vi.fn(), updateLeaveType: vi.fn() };
});

// jsdom has no matchMedia/ResizeObserver/document.fonts; LeaveTypeDialog's useMediaQuery, Mantine's Select/Combobox,
// and the autosize Textarea (description field) each need one of these mounted to render without throwing.
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

function renderDialog(editing: LeaveType | null) {
  const ruleSet: LeaveRuleSet = {
    id: "rule-set-1",
    name: "Varsayılan",
    isDefault: true,
    isSystemSeed: false,
    unpaidLeaveShiftsAccrual: false,
    assignedEmployeeCount: 0,
    types: editing ? [editing] : [],
  };
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const onClose = vi.fn();

  const view = render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <LeaveTypeDialog ruleSet={ruleSet} editing={editing} onClose={onClose} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );

  return { onClose, ...view };
}

function seededAnnualType(): LeaveType {
  return {
    id: "type-1",
    sortOrder: 0,
    settings: {
      code: "annual",
      // Seeded system rule sets store the resource key, not a display string (see leave-shared.tsx useLeaveName).
      name: "leave.type.annual",
      description: null,
      isPaid: true,
      limitType: "perYear",
      perRequestMaxDays: null,
      annualDays: 14,
      ageExceptionEnabled: false,
      ageExceptionMinDays: null,
      negativeBalance: "none",
      negativeBalanceLimitDays: null,
      accrualFrequency: "yearly",
      accrualTiming: "endOfPeriod",
      accrualBasis: "hireDate",
      carryOverMode: "reset",
      carryOverMaxDays: null,
      seniorityTiers: [],
      minConsecutiveDays: null,
      maxConsecutiveDays: null,
      unit: "fullDay",
      requiresDescription: false,
      requiresSubstitute: false,
      requiresDocument: false,
      waitingPeriodMonths: 0,
      countsNonWorkingDays: false,
      countsTowardAccrualService: true,
      showTypeNameOnCalendar: true,
      isSensitive: false,
      allowSelfServiceRequest: true,
      isDisabled: false,
      isStatutory: false,
    },
  };
}

describe("LeaveTypeDialog (senseik-021 form modal alignment)", () => {
  it("opens left-aligned via the shared FormModal wrapper (inner justify-content: flex-start)", () => {
    renderDialog(seededAnnualType());

    // Mantine's ModalBaseContent renders the "inner" flex wrapper as the dialog's direct parent
    // (FocusTrap clones its child rather than adding a DOM node of its own).
    const inner = screen.getByRole("dialog").parentElement;
    expect(inner).not.toBeNull();
    expect(inner?.style.justifyContent).toBe("flex-start");
  });
});

describe("LeaveTypeDialog (senseik-008 Bug 1)", () => {
  it("shows the resolved leave name, not the raw seed key, in the name field", () => {
    renderDialog(seededAnnualType());

    const nameInput = screen.getByRole("textbox", { name: /Tür adı/ }) as HTMLInputElement;
    expect(nameInput.value).toBe("Yıllık izin");
    expect(nameInput.value).not.toContain("leave.type.annual");
    expect(screen.queryByText("leave.type.annual")).toBeNull();
  });

  it("saves the resolved display name (not the raw key) when the user does not touch the name field", async () => {
    const user = userEvent.setup();
    renderDialog(seededAnnualType());

    await user.click(screen.getByRole("button", { name: /Kaydet/ }));

    expect(updateLeaveType).toHaveBeenCalledTimes(1);
    const [, , payload] = vi.mocked(updateLeaveType).mock.calls[0];
    expect(payload.name).toBe("Yıllık izin");
    expect(payload.name).not.toBe("leave.type.annual");
  });

  it("saves the user's edited value verbatim (raw override), not a re-resolved key", async () => {
    const user = userEvent.setup();
    renderDialog(seededAnnualType());

    const nameInput = screen.getByRole("textbox", { name: /Tür adı/ });
    await user.clear(nameInput);
    await user.type(nameInput, "Özel yıllık izin türü");
    await user.click(screen.getByRole("button", { name: /Kaydet/ }));

    expect(updateLeaveType).toHaveBeenCalledTimes(1);
    const [, , payload] = vi.mocked(updateLeaveType).mock.calls[0];
    expect(payload.name).toBe("Özel yıllık izin türü");
  });
});
