import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trLeave from "../../../public/locales/tr/leave.json";
import trCommon from "../../../public/locales/tr/common.json";
import { LeaveRequestDialog, type LeaveRequestTypeOption } from "./leave-request-dialog";
import type { LeaveRequestInput } from "@/services/leave.service";

// Denetim raporu A5: a type with `requiresSubstitute` shows a required "Vekil" picker and the form sends `substituteEmployeeId`.
// The real pickers hit the API / open portalled calendars; light stand-ins keep this test about the dialog's own wiring.
vi.mock("@/components/employees/employee-picker", () => ({
  default: ({ label, onChange, excludeId, useDirectory }: { label: string; onChange: (v: { id: string; displayName: string }) => void; excludeId?: string; useDirectory?: boolean }) => (
    <button type="button" data-exclude={excludeId} data-directory={String(Boolean(useDirectory))} onClick={() => onChange({ id: "employee-2", displayName: "Ada Lovelace" })}>
      {label}
    </button>
  ),
}));
vi.mock("@/components/ui/date-picker", () => ({
  DatePicker: ({ id, value, onChange }: { id: string; value: string; onChange: (v: string) => void }) => (
    <input aria-label={id} value={value} onChange={(event) => onChange(event.target.value)} />
  ),
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

function typeOption(overrides: Partial<LeaveRequestTypeOption> = {}): LeaveRequestTypeOption {
  return {
    id: "type-1",
    name: "Evlilik izni",
    description: null,
    requiresDescription: false,
    perRequestMaxDays: null,
    availableBalance: null,
    unit: "fullDay",
    ...overrides,
  };
}

function renderDialog(type: LeaveRequestTypeOption) {
  const save = vi.fn<(input: LeaveRequestInput, bypassApproval: boolean) => Promise<unknown>>().mockResolvedValue(undefined);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <LeaveRequestDialog
            editing={null}
            types={[type]}
            title="İzin talebi"
            description="Açıklama"
            submitLabel="Gönder"
            previewScope="me"
            preview={() => Promise.resolve({ days: 2, weeklyRestDays: 0, publicHolidayDays: 0, warnings: [] })}
            save={save}
            requesterEmployeeId="employee-1"
            onClose={vi.fn()}
            onSaved={vi.fn()}
          />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
  return { save };
}

async function fillDates(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("leave-start"), "2026-10-05");
  await user.type(screen.getByLabelText("leave-end"), "2026-10-06");
}

describe("LeaveRequestDialog substitute (denetim A5)", () => {
  it("shows the substitute picker (directory source, requester excluded) only for a type that requires one", () => {
    renderDialog(typeOption({ requiresSubstitute: true }));

    const picker = screen.getByRole("button", { name: "Vekil" });
    expect(picker.getAttribute("data-directory")).toBe("true");
    expect(picker.getAttribute("data-exclude")).toBe("employee-1");
  });

  it("does not show the picker for an ordinary type", () => {
    renderDialog(typeOption());

    expect(screen.queryByRole("button", { name: "Vekil" })).toBeNull();
  });

  it("blocks the submit until a substitute is chosen", async () => {
    const user = userEvent.setup();
    const { save } = renderDialog(typeOption({ requiresSubstitute: true }));
    await fillDates(user);

    await user.click(screen.getByRole("button", { name: "Gönder" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Bu izin türünde vekil seçimi zorunludur.");
    expect(save).not.toHaveBeenCalled();
  });

  it("sends the chosen substitute's id", async () => {
    const user = userEvent.setup();
    const { save } = renderDialog(typeOption({ requiresSubstitute: true }));
    await fillDates(user);

    await user.click(screen.getByRole("button", { name: "Vekil" }));
    await user.click(screen.getByRole("button", { name: "Gönder" }));

    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0][0]).toMatchObject({ leaveTypeId: "type-1", substituteEmployeeId: "employee-2" });
  });

  it("sends no substitute for an ordinary type", async () => {
    const user = userEvent.setup();
    const { save } = renderDialog(typeOption());
    await fillDates(user);

    await user.click(screen.getByRole("button", { name: "Gönder" }));

    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0][0].substituteEmployeeId).toBeUndefined();
  });

  it("reminds the requester to hand in the document when the type requires one", () => {
    renderDialog(typeOption({ requiresDocument: true }));

    expect(screen.getByText(/belge \(rapor vb\.\) gerekir/)).toBeTruthy();
  });
});
