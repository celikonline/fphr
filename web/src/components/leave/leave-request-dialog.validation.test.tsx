import { beforeAll, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trLeave from "../../../public/locales/tr/leave.json";
import trCommon from "../../../public/locales/tr/common.json";
import { LeaveRequestDialog, type LeaveRequestTypeOption } from "./leave-request-dialog";
import type { LeaveRequestInput } from "@/services/leave.service";

// D-12 (ik-surec-testi-bulgu-raporu-2026-09-20): an empty submit reports every missing field next to that field,
// picking a start date fills an empty end date, and the modal's close icon has an accessible name.
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
const sick: LeaveRequestTypeOption = { ...annual, id: "type-sick", name: "Hastalık izni" };

function renderDialog(types: LeaveRequestTypeOption[]) {
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
            types={types}
            title="İzin talebi"
            description="Açıklama"
            submitLabel="Gönder"
            previewScope="me"
            preview={() =>
              Promise.resolve({ days: 1, weeklyRestDays: 0, publicHolidayDays: 0, warnings: [] })
            }
            save={save}
            onClose={vi.fn()}
            onSaved={vi.fn()}
          />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
  return { save };
}

describe("LeaveRequestDialog field validation (D-12)", () => {
  it("reports the type, start and end fields together on an empty submit", async () => {
    const user = userEvent.setup();
    renderDialog([annual, sick]);

    await user.click(screen.getByRole("button", { name: "Gönder" }));

    expect(await screen.findByText(trLeave.form.validation.typeRequired)).toBeTruthy();
    expect(screen.getByText(trLeave.form.validation.startRequired)).toBeTruthy();
    expect(screen.getByText(trLeave.form.validation.endRequired)).toBeTruthy();
  });

  it("clears a field's error as soon as that field is filled", async () => {
    const user = userEvent.setup();
    renderDialog([annual]);

    await user.click(screen.getByRole("button", { name: "Gönder" }));
    expect(await screen.findByText(trLeave.form.validation.startRequired)).toBeTruthy();

    await user.type(screen.getByLabelText("leave-start"), "2026-10-05");

    await waitFor(() =>
      expect(screen.queryByText(trLeave.form.validation.startRequired)).toBeNull()
    );
    expect(screen.queryByText(trLeave.form.validation.endRequired)).toBeNull();
  });

  it("copies the picked start date into an empty end date", async () => {
    const user = userEvent.setup();
    const { save } = renderDialog([annual]);

    fireEvent.change(screen.getByLabelText("leave-start"), { target: { value: "2026-10-05" } });
    expect((screen.getByLabelText("leave-end") as HTMLInputElement).value).toBe("2026-10-05");

    await user.click(screen.getByRole("button", { name: "Gönder" }));
    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0][0]).toMatchObject({ startDate: "2026-10-05", endDate: "2026-10-05" });
  });

  it("keeps an end date the user already chose when the start date changes", async () => {
    const user = userEvent.setup();
    renderDialog([annual]);

    await user.type(screen.getByLabelText("leave-end"), "2026-10-09");
    await user.type(screen.getByLabelText("leave-start"), "2026-10-05");

    expect((screen.getByLabelText("leave-end") as HTMLInputElement).value).toBe("2026-10-09");
  });

  it("gives the modal close icon an accessible name", () => {
    renderDialog([annual]);

    expect(screen.getByRole("button", { name: trCommon.close })).toBeTruthy();
  });
});
