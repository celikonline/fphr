import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { AxiosError } from "axios";
import trPayroll from "../../../public/locales/tr/payroll.json";
import trCommon from "../../../public/locales/tr/common.json";
import { PeriodPayItems } from "./period-pay-items";
import type { PayrollPeriod } from "@/services/payroll.service";
import {
  createPayItemAssignments,
  deletePayItemAssignment,
  listPayItemAssignments,
  listPayItems,
  listPeriodPayItemLines,
  updatePayItemAssignment,
  type PayItemAssignment,
} from "@/services/payroll-pay-items.service";

const permissions = { manage: true };
vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return { ...actual, usePermission: (key: string) => key === "payroll.payitem.manage" && permissions.manage };
});

vi.mock("@/components/employees/employee-picker", () => ({
  default: ({ onChange }: { onChange: (value: { id: string; displayName: string } | null) => void }) => (
    <button type="button" onClick={() => onChange({ id: "e9", displayName: "Zeynep Kaya" })}>
      pick-employee
    </button>
  ),
}));

vi.mock("@/services/payroll-pay-items.service", () => ({
  listPayItemAssignments: vi.fn(),
  createPayItemAssignments: vi.fn(async () => ["a1"]),
  updatePayItemAssignment: vi.fn(async () => undefined),
  deletePayItemAssignment: vi.fn(async () => undefined),
  listPeriodPayItemLines: vi.fn(),
  listPayItems: vi.fn(),
}));

const ASSIGNMENT: PayItemAssignment = {
  id: "a1",
  employeeId: "e1",
  employeeName: "Ayşe Yılmaz",
  payItemDefinitionId: "d1",
  code: "YEMEK",
  name: "Yemek yardımı",
  value: 150,
  fromYear: 2026,
  fromMonth: 1,
};

const OPEN: PayrollPeriod = { id: "p1", year: 2026, month: 10, status: "open" };

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
  Element.prototype.scrollIntoView = () => {};
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

beforeEach(() => {
  permissions.manage = true;
  vi.mocked(listPayItemAssignments).mockReset().mockResolvedValue([ASSIGNMENT]);
  vi.mocked(createPayItemAssignments).mockReset().mockResolvedValue(["a1"]);
  vi.mocked(updatePayItemAssignment).mockReset().mockResolvedValue(undefined);
  vi.mocked(deletePayItemAssignment).mockReset().mockResolvedValue(undefined);
  vi.mocked(listPeriodPayItemLines).mockReset().mockResolvedValue([]);
  vi.mocked(listPayItems)
    .mockReset()
    .mockResolvedValue([{ id: "d1", code: "YEMEK", name: "Yemek yardımı", direction: "earning", amountBasis: "gross", isActive: true, defaultValue: 100 }]);
});

function renderCard(period: PayrollPeriod = OPEN) {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "payroll",
    ns: ["payroll", "common"],
    resources: { tr: { payroll: trPayroll, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <PeriodPayItems period={period} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

function serverError(detail: string): AxiosError {
  return new AxiosError("fail", "ERR_BAD_REQUEST", undefined, undefined, {
    status: 409,
    statusText: "Conflict",
    headers: {},
    config: {} as never,
    data: { detail },
  });
}

async function fillAndSubmitAssignment(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: "Ödeme türü ata" }));
  const dialog = await screen.findByRole("dialog");
  await user.click(within(dialog).getByRole("combobox", { name: /Ödeme türü/ }));
  await user.click(await screen.findByRole("option", { name: "YEMEK - Yemek yardımı", hidden: true }));
  await user.click(within(dialog).getByRole("button", { name: "pick-employee" }));
  await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));
}

describe("PeriodPayItems", () => {
  it("lists the period assignments with value and range, and tolerates missing optional fields", async () => {
    vi.mocked(listPayItemAssignments).mockResolvedValue([ASSIGNMENT, { id: "a2", employeeId: "e2", payItemDefinitionId: "d2", code: "PRIM", fromYear: 2026, fromMonth: 3, toYear: 2026, toMonth: 12 }]);
    renderCard();

    const row = (await screen.findByText("Ayşe Yılmaz")).closest("tr")!;
    expect(within(row).getByText("YEMEK - Yemek yardımı")).toBeInTheDocument();
    expect(within(row).getByText("01/2026 - süresiz")).toBeInTheDocument();
    const second = (await screen.findByText("PRIM")).closest("tr")!;
    expect(within(second).getByText("Varsayılan")).toBeInTheDocument();
    expect(within(second).getByText("03/2026 - 12/2026")).toBeInTheDocument();
    expect(listPayItemAssignments).toHaveBeenCalledWith({ periodId: "p1" });
  });

  it("assigns a pay item to several employees from the modal", async () => {
    const user = userEvent.setup();
    renderCard();

    await fillAndSubmitAssignment(user);

    await waitFor(() =>
      expect(createPayItemAssignments).toHaveBeenCalledWith({
        payItemDefinitionId: "d1",
        employeeIds: ["e9"],
        value: null,
        fromYear: 2026,
        fromMonth: 10,
        toYear: null,
        toMonth: null,
      }),
    );
  });

  it("shows the server message when the assignment is rejected", async () => {
    vi.mocked(createPayItemAssignments).mockRejectedValue(serverError("Bu çalışana aynı ödeme türü bu aralıkta zaten atanmış."));
    const user = userEvent.setup();
    renderCard();

    await fillAndSubmitAssignment(user);

    expect(await screen.findByText("Bu çalışana aynı ödeme türü bu aralıkta zaten atanmış.")).toBeInTheDocument();
  });

  it("edits the value and end month of an assignment", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("button", { name: "YEMEK atamasını düzenle" }));
    const dialog = await screen.findByRole("dialog");
    const value = within(dialog).getByLabelText("Değer");
    await user.clear(value);
    await user.type(value, "250");
    await user.type(within(dialog).getByLabelText("Bitiş ayı"), "2026-12");
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    await waitFor(() => expect(updatePayItemAssignment).toHaveBeenCalledWith("a1", { value: 250, toYear: 2026, toMonth: 12 }));
  });

  it("asks for confirmation before deleting and shows the server error", async () => {
    vi.mocked(deletePayItemAssignment).mockRejectedValue(serverError("Kapalı döneme ait atama silinemez."));
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("button", { name: "YEMEK atamasını sil" }));
    expect(deletePayItemAssignment).not.toHaveBeenCalled();
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Sil" }));

    await waitFor(() => expect(deletePayItemAssignment).toHaveBeenCalledWith("a1"));
    expect(await within(dialog).findByText("Kapalı döneme ait atama silinemez.")).toBeInTheDocument();
  });

  it("is list-only when the period is not open", async () => {
    renderCard({ ...OPEN, status: "approved" });

    expect(await screen.findByText("Ayşe Yılmaz")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Ödeme türü ata" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "YEMEK atamasını düzenle" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "YEMEK atamasını sil" })).not.toBeInTheDocument();
  });

  it("hides the actions without the manage permission", async () => {
    permissions.manage = false;
    renderCard();

    expect(await screen.findByText("Ayşe Yılmaz")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Ödeme türü ata" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "YEMEK atamasını sil" })).not.toBeInTheDocument();
  });

  it("shows an empty state when nothing is assigned", async () => {
    vi.mocked(listPayItemAssignments).mockResolvedValue([]);
    renderCard();

    expect(await screen.findByTestId("assignments-empty")).toHaveTextContent("Bu dönem için atanmış bir ödeme türü yok.");
  });

  it("shows the calculated lines of the latest run", async () => {
    vi.mocked(listPeriodPayItemLines).mockResolvedValue([
      { employeeId: "e1", employeeName: "Ayşe Yılmaz", code: "YEMEK", name: "Yemek yardımı", amount: 1500, amountBasis: "net", netTarget: 1200 },
      { employeeId: "e2", code: "PRIM" },
    ]);
    renderCard();

    expect(await screen.findByText("Hesaplanan ödeme türü kalemleri")).toBeInTheDocument();
    await waitFor(() => expect(screen.getAllByText("YEMEK - Yemek yardımı").length).toBe(2));
    expect(screen.queryByTestId("pay-item-lines-empty")).not.toBeInTheDocument();
    expect(listPeriodPayItemLines).toHaveBeenCalledWith("p1");
  });

  it("shows an empty state when the period has no calculated lines", async () => {
    renderCard();

    expect(await screen.findByTestId("pay-item-lines-empty")).toHaveTextContent(/hesaplanmış ödeme türü kalemi yok/);
  });
});
