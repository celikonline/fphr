import { beforeAll, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trLeave from "../../../public/locales/tr/leave.json";
import trCommon from "../../../public/locales/tr/common.json";
import { adjustLeaveBalance } from "@/services/leave.service";
import { AdjustBalanceDialog } from "./leave-section";

vi.mock("@/services/leave.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/leave.service")>(
    "@/services/leave.service"
  );
  return { ...actual, adjustLeaveBalance: vi.fn() };
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

const TWO_TYPES = [
  { id: "type-1", name: "Yıllık izin" },
  { id: "type-2", name: "Mazeret izni" },
];

function renderDialog(
  onClose = vi.fn(),
  typeOptions: { id: string; name: string; currentBalance?: number }[] = TWO_TYPES
) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <AdjustBalanceDialog
            employeeId="employee-1"
            typeOptions={typeOptions}
            onClose={onClose}
            onSaved={vi.fn()}
          />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
  return { onClose };
}

describe("AdjustBalanceDialog", () => {
  it("marks every empty required field at once on submit (B6)", async () => {
    renderDialog();

    fireEvent.click(screen.getByRole("button", { name: trCommon.save }));

    const messages = trLeave.actions.validation;
    expect(await screen.findByText(messages.typeRequired)).not.toBeNull();
    expect(screen.getByText(messages.daysInvalid)).not.toBeNull();
    expect(screen.getByText(messages.reasonRequired)).not.toBeNull();
  });

  it("clears a field's message as soon as that field is edited", async () => {
    renderDialog();
    fireEvent.click(screen.getByRole("button", { name: trCommon.save }));
    await screen.findByText(trLeave.actions.validation.reasonRequired);

    fireEvent.change(screen.getByLabelText(new RegExp(trLeave.actions.reason)), {
      target: { value: "Düzeltme" },
    });

    await waitFor(() =>
      expect(screen.queryByText(trLeave.actions.validation.reasonRequired)).toBeNull()
    );
    expect(screen.getByText(trLeave.actions.validation.typeRequired)).not.toBeNull();
  });

  it("asks for confirmation before a correction that drives the balance negative (O-11)", async () => {
    vi.mocked(adjustLeaveBalance).mockResolvedValue(undefined as never);
    renderDialog(vi.fn(), [{ id: "type-1", name: "Yıllık izin", currentBalance: 3 }]);

    fireEvent.change(screen.getByLabelText(/^Gün/), { target: { value: "-5" } });
    fireEvent.change(screen.getByLabelText(new RegExp(trLeave.actions.reason)), {
      target: { value: "Düzeltme" },
    });
    fireEvent.click(screen.getByRole("button", { name: trCommon.save }));

    expect(await screen.findByText(trLeave.actions.warnings.title)).not.toBeNull();
    expect(screen.getByText(/-2 gün olacak/)).not.toBeNull();
    expect(adjustLeaveBalance).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: trLeave.actions.warnings.confirm }));
    await waitFor(() => expect(adjustLeaveBalance).toHaveBeenCalledTimes(1));
  });

  it("saves immediately when nothing looks risky", async () => {
    vi.mocked(adjustLeaveBalance).mockClear();
    vi.mocked(adjustLeaveBalance).mockResolvedValue(undefined as never);
    renderDialog(vi.fn(), [{ id: "type-1", name: "Yıllık izin", currentBalance: 10 }]);

    fireEvent.change(screen.getByLabelText(/^Gün/), { target: { value: "-2" } });
    fireEvent.change(screen.getByLabelText(new RegExp(trLeave.actions.reason)), {
      target: { value: "Düzeltme" },
    });
    fireEvent.click(screen.getByRole("button", { name: trCommon.save }));

    await waitFor(() => expect(adjustLeaveBalance).toHaveBeenCalledTimes(1));
    expect(screen.queryByText(trLeave.actions.warnings.title)).toBeNull();
  });

  it("opens left-aligned and ignores overlay clicks (B2/B3)", () => {
    const { onClose } = renderDialog();

    const inner = screen.getByRole("dialog").parentElement as HTMLElement;
    fireEvent.mouseDown(inner);
    fireEvent.click(inner);

    expect(inner.style.justifyContent).toBe("flex-start");
    expect(onClose).not.toHaveBeenCalled();
  });
});
