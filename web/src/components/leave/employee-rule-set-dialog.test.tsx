import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trLeave from "../../../public/locales/tr/leave.json";
import trCommon from "../../../public/locales/tr/common.json";
import { ChangeEmployeeRuleSetDialog } from "./employee-rule-set-dialog";
import { listLeaveRuleSets, type LeaveRuleSet } from "@/services/leave.service";

// senseik-021: ChangeEmployeeRuleSetDialog is one of the form/edit modals that should open
// left-aligned (via the shared FormModal wrapper), not Mantine's default horizontally-centered
// placement — unlike the confirm/delete dialogs, which stay centered.
vi.mock("@/services/leave.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/leave.service")>("@/services/leave.service");
  return { ...actual, listLeaveRuleSets: vi.fn(), assignLeaveRuleSet: vi.fn() };
});

// jsdom has no matchMedia/ResizeObserver/document.fonts; Mantine's Select/Combobox needs one of
// these mounted to render without throwing.
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

function renderDialog() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const onClose = vi.fn();
  const onChanged = vi.fn();

  const view = render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <ChangeEmployeeRuleSetDialog
            employeeId="employee-1"
            employeeName="Ada Lovelace"
            currentRuleSetId="rule-set-1"
            onClose={onClose}
            onChanged={onChanged}
          />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );

  return { onClose, onChanged, ...view };
}

describe("ChangeEmployeeRuleSetDialog (senseik-021 form modal alignment)", () => {
  it("opens left-aligned via the shared FormModal wrapper (inner justify-content: flex-start)", () => {
    const ruleSets: LeaveRuleSet[] = [
      {
        id: "rule-set-1",
        name: "Varsayılan",
        isDefault: true,
        isSystemSeed: false,
        unpaidLeaveShiftsAccrual: false,
        assignedEmployeeCount: 1,
        types: [],
      },
    ];
    vi.mocked(listLeaveRuleSets).mockResolvedValue(ruleSets);

    renderDialog();

    // Mantine's ModalBaseContent renders the "inner" flex wrapper as the dialog's direct parent
    // (FocusTrap clones its child rather than adding a DOM node of its own).
    const inner = screen.getByRole("dialog").parentElement;
    expect(inner).not.toBeNull();
    expect(inner?.style.justifyContent).toBe("flex-start");
  });

  it("renders the dialog title and employee name", async () => {
    vi.mocked(listLeaveRuleSets).mockResolvedValue([]);
    renderDialog();

    expect(await screen.findByRole("dialog")).not.toBeNull();
    expect(screen.getByText(/Ada Lovelace/)).not.toBeNull();
  });
});
