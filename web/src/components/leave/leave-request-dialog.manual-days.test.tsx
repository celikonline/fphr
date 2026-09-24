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

// TK-014: a manager adding leave on behalf of an employee can override the system-calculated day count.
vi.mock("@/components/ui/date-picker", () => ({
  DatePicker: ({
    id,
    value,
    onChange,
  }: {
    id: string;
    value: string;
    onChange: (v: string) => void;
  }) => <input aria-label={id} value={value} onChange={(event) => onChange(event.target.value)} />,
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

const annual: LeaveRequestTypeOption = {
  id: "type-annual",
  name: "Yıllık izin",
  description: null,
  requiresDescription: false,
  perRequestMaxDays: null,
  availableBalance: null,
  unit: "fullDay",
};

function renderDialog(allowManualDays: boolean) {
  const save = vi
    .fn<(input: LeaveRequestInput, bypassApproval: boolean) => Promise<unknown>>()
    .mockResolvedValue(undefined);
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <LeaveRequestDialog
            editing={null}
            types={[annual]}
            title="İzin ekle"
            description="Açıklama"
            submitLabel="Gönder"
            previewScope="employee-1"
            preview={() => Promise.resolve({ days: 2, weeklyRestDays: 0, publicHolidayDays: 0, warnings: [] })}
            save={save}
            allowManualDays={allowManualDays}
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

describe("LeaveRequestDialog manual day count (TK-014)", () => {
  it("shows the manual day count field, empty by default, when allowManualDays is set", () => {
    renderDialog(true);

    const field = screen.getByLabelText(trLeave.form.manualDays) as HTMLInputElement;
    expect(field).toBeTruthy();
    expect(field.value).toBe("");
  });

  it("hides the manual day count field when allowManualDays is not set", () => {
    renderDialog(false);

    expect(screen.queryByLabelText(trLeave.form.manualDays)).toBeNull();
  });

  it("omits manualDays from the payload when the field is left untouched", async () => {
    const user = userEvent.setup();
    const { save } = renderDialog(true);
    await fillDates(user);

    await user.click(screen.getByRole("button", { name: "Gönder" }));

    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    // `undefined` (not omitted) is fine here: axios/JSON.stringify drops undefined keys, so the wire payload has no manualDays.
    expect((save.mock.calls[0][0] as LeaveRequestInput & { manualDays?: number }).manualDays).toBeUndefined();
  });

  it("sends the manually entered day count in the save payload", async () => {
    const user = userEvent.setup();
    const { save } = renderDialog(true);
    await fillDates(user);

    await user.type(screen.getByLabelText(trLeave.form.manualDays), "3.5");
    await user.click(screen.getByRole("button", { name: "Gönder" }));

    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0][0]).toMatchObject({ manualDays: 3.5 });
  });
});
