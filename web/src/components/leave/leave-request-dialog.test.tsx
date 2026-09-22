import { beforeAll, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trLeave from "../../../public/locales/tr/leave.json";
import trCommon from "../../../public/locales/tr/common.json";
import { LeaveRequestDialog, type LeaveRequestTypeOption } from "./leave-request-dialog";
import type { LeaveRequest, LeaveRequestPreview } from "@/services/leave.service";

// jsdom has no matchMedia/ResizeObserver/document.fonts; Mantine's Select/pickers need them mounted.
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

const fullDayType: LeaveRequestTypeOption = {
  id: "type-annual",
  name: "Yıllık izin",
  description: null,
  requiresDescription: false,
  perRequestMaxDays: null,
  availableBalance: null,
  unit: "fullDay",
};
const hourlyType: LeaveRequestTypeOption = { ...fullDayType, id: "type-hourly", name: "Saatlik izin", unit: "hourly" };

const previewResult = { days: 1, weeklyRestDays: 0, publicHolidayDays: 0, warnings: [] } as unknown as LeaveRequestPreview;

function renderDialog(options: { types: LeaveRequestTypeOption[]; prefill?: LeaveRequest | null; onClose?: () => void }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const preview = vi.fn().mockResolvedValue(previewResult);
  const save = vi.fn().mockResolvedValue(undefined);
  const onClose = options.onClose ?? vi.fn();
  render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <LeaveRequestDialog
            editing={null}
            prefill={options.prefill ?? null}
            types={options.types}
            title="İzin talebi"
            description="Açıklama"
            submitLabel="Gönder"
            previewScope="me"
            preview={preview}
            save={save}
            onClose={onClose}
            onSaved={vi.fn()}
          />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
  return { preview, save, onClose };
}

function prefillRequest(overrides: Partial<LeaveRequest>): LeaveRequest {
  return {
    id: "req-1",
    employeeId: "emp-1",
    leaveTypeId: hourlyType.id,
    leaveTypeCode: "annual",
    startDate: "2026-09-21",
    endDate: "2026-09-21",
    calculatedDays: 1,
    status: "rejected",
    source: "self",
    revisionNo: 1,
    submittedAt: "2026-09-20T08:00:00Z",
    ...overrides,
  } as LeaveRequest;
}

describe("LeaveRequestDialog", () => {
  it("shows an error under each empty date field instead of one form-level message (B4, D-12)", async () => {
    renderDialog({ types: [fullDayType] });

    fireEvent.click(screen.getByRole("button", { name: "Gönder" }));

    expect(await screen.findByText(trLeave.form.validation.startRequired)).not.toBeNull();
    expect(screen.getByText(trLeave.form.validation.endRequired)).not.toBeNull();
  });

  it("keeps the dialog open when the overlay is clicked (B3)", () => {
    const onClose = vi.fn();
    renderDialog({ types: [fullDayType], onClose });

    const inner = screen.getByRole("dialog").parentElement as HTMLElement;
    fireEvent.mouseDown(inner);
    fireEvent.click(inner);

    expect(onClose).not.toHaveBeenCalled();
    expect(inner.style.justifyContent).toBe("flex-start");
  });

  it("hides the day preview and explains the range when the hourly range is reversed (B5)", async () => {
    const { preview } = renderDialog({
      types: [hourlyType],
      prefill: prefillRequest({ startTime: "10:00:00", endTime: "09:00:00" }),
    });

    expect(await screen.findByText(trLeave.form.validation.timeRequired)).not.toBeNull();
    // Wait past the 300 ms debounce: the preview must neither be requested nor rendered.
    await new Promise((resolve) => setTimeout(resolve, 400));
    expect(preview).not.toHaveBeenCalled();
    expect(screen.queryByText(/Hesaplanan gün/)).toBeNull();
  });

  it("shows the day preview when the hourly range is valid", async () => {
    renderDialog({ types: [hourlyType], prefill: prefillRequest({ startTime: "09:00:00", endTime: "10:00:00" }) });

    expect(await screen.findByText(/Hesaplanan gün: 1/)).not.toBeNull();
    expect(screen.queryByText(trLeave.form.validation.timeRequired)).toBeNull();
  });

  it("gives every hourly control a unique id and keeps label-to-input association (B8)", () => {
    renderDialog({ types: [hourlyType], prefill: prefillRequest({ startTime: "09:00:00", endTime: "10:00:00" }) });

    for (const id of ["leave-hour-start", "leave-hour-end", "leave-start"]) {
      expect(document.querySelectorAll(`[id="${id}"]`)).toHaveLength(1);
    }
    expect((screen.getByLabelText(new RegExp(trLeave.form.hourlyStart)) as HTMLElement).id).toBe("leave-hour-start");
    expect((screen.getByLabelText(new RegExp(trLeave.form.hourlyEnd)) as HTMLElement).id).toBe("leave-hour-end");
  });
});
