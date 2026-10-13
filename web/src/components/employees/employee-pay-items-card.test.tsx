import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trPayroll from "../../../public/locales/tr/payroll.json";
import trCommon from "../../../public/locales/tr/common.json";
import { EmployeePayItemsCard } from "./employee-pay-items-card";
import { createPayItemAssignments, listPayItemAssignments, listPayItems } from "@/services/payroll-pay-items.service";

const permissions = { manage: true };
vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return { ...actual, usePermission: (key: string) => key === "payroll.payitem.manage" && permissions.manage };
});

vi.mock("@/services/payroll-pay-items.service", () => ({
  listPayItemAssignments: vi.fn(),
  createPayItemAssignments: vi.fn(async () => ["a2"]),
  deletePayItemAssignment: vi.fn(async () => undefined),
  listPayItems: vi.fn(),
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
  Element.prototype.scrollIntoView = () => {};
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

beforeEach(() => {
  permissions.manage = true;
  vi.mocked(listPayItemAssignments)
    .mockReset()
    .mockResolvedValue([{ id: "a1", employeeId: "e1", payItemDefinitionId: "d1", code: "YEMEK", name: "Yemek yardımı", value: 150, fromYear: 2026, fromMonth: 1 }]);
  vi.mocked(createPayItemAssignments).mockClear();
  vi.mocked(listPayItems)
    .mockReset()
    .mockResolvedValue([{ id: "d1", code: "YEMEK", name: "Yemek yardımı", direction: "earning", amountBasis: "gross", isActive: true }]);
});

function renderCard() {
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
          <EmployeePayItemsCard employee={{ id: "e1", displayName: "Ayşe Yılmaz" }} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("EmployeePayItemsCard", () => {
  it("lists the employee's recurring assignments", async () => {
    renderCard();

    const row = (await screen.findByText("YEMEK - Yemek yardımı")).closest("tr")!;
    expect(within(row).getByText("01/2026 - süresiz")).toBeInTheDocument();
    expect(listPayItemAssignments).toHaveBeenCalledWith({ employeeId: "e1" });
  });

  it("is read-only without the manage permission", async () => {
    permissions.manage = false;
    renderCard();

    expect(await screen.findByText("YEMEK - Yemek yardımı")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Ödeme türü ata" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "YEMEK atamasını sil" })).not.toBeInTheDocument();
  });

  it("assigns to this employee only, without an employee picker", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("button", { name: "Ödeme türü ata" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("combobox", { name: /Ödeme türü/ }));
    await user.click(await screen.findByRole("option", { name: "YEMEK - Yemek yardımı", hidden: true }));
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    await waitFor(() => expect(createPayItemAssignments).toHaveBeenCalledWith(expect.objectContaining({ payItemDefinitionId: "d1", employeeIds: ["e1"] })));
  });

  it("shows the definition direction badge (deduction via the definition list)", async () => {
    vi.mocked(listPayItemAssignments).mockResolvedValue([
      { id: "a1", employeeId: "e1", payItemDefinitionId: "d1", code: "YEMEK", name: "Yemek yardımı", fromYear: 2026, fromMonth: 1 },
      { id: "a2", employeeId: "e1", payItemDefinitionId: "d2", code: "KANTIN", name: "Kantin", fromYear: 2026, fromMonth: 1 },
    ]);
    vi.mocked(listPayItems).mockResolvedValue([
      { id: "d1", code: "YEMEK", name: "Yemek yardımı", direction: "earning", amountBasis: "gross", isActive: true },
      { id: "d2", code: "KANTIN", name: "Kantin", direction: "deduction", amountBasis: "gross", isActive: true },
    ]);
    renderCard();

    const kantin = (await screen.findByText("KANTIN - Kantin")).closest("tr")!;
    await waitFor(() => expect(within(kantin).getByText("Kesinti")).toBeInTheDocument());
    expect(within(screen.getByText("YEMEK - Yemek yardımı").closest("tr")!).getByText("Kazanç")).toBeInTheDocument();
  });

  it("shows an empty state", async () => {
    vi.mocked(listPayItemAssignments).mockResolvedValue([]);
    renderCard();

    expect(await screen.findByTestId("employee-assignments-empty")).toBeInTheDocument();
  });
});
