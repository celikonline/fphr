import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trPayroll from "../../../public/locales/tr/payroll.json";
import trCommon from "../../../public/locales/tr/common.json";
import { EmployeeIncentivesCard, hasEffectiveSgkAssignment } from "./employee-incentives-card";
import {
  createIncentiveAssignments,
  deleteIncentiveAssignment,
  listIncentiveAssignments,
  listIncentiveDefinitions,
  updateIncentiveAssignment,
  type IncentiveAssignment,
} from "@/services/payroll-incentives.service";

const permissions = { manage: true };
vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return { ...actual, usePermission: (key: string) => key === "payroll.incentive.manage" && permissions.manage };
});

vi.mock("@/services/payroll-incentives.service", () => ({
  listIncentiveAssignments: vi.fn(),
  createIncentiveAssignments: vi.fn(async () => ["n1"]),
  updateIncentiveAssignment: vi.fn(async () => undefined),
  deleteIncentiveAssignment: vi.fn(async () => undefined),
  listIncentiveDefinitions: vi.fn(),
}));

const APPROVED: IncentiveAssignment = {
  id: "a1",
  employeeId: "e1",
  employeeName: "Ayşe Yılmaz",
  incentiveDefinitionId: "d1",
  code: "SGK_5510_MD81",
  name: "5510 md.81",
  declarationCode: "05510",
  kind: "employerSgkRateDiscount",
  isSmmmApproved: true,
  fromYear: 2026,
  fromMonth: 1,
  note: "Danışman onayı",
};
const UNAPPROVED: IncentiveAssignment = {
  id: "a2",
  employeeId: "e1",
  incentiveDefinitionId: "d2",
  code: "ARGE_5746_GV_TASLAK",
  kind: "incomeTaxWithholdingRelief",
  isSmmmApproved: false,
  fromYear: 2026,
  fromMonth: 3,
  toYear: 2026,
  toMonth: 12,
};

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
  vi.mocked(listIncentiveAssignments).mockReset().mockResolvedValue([APPROVED, UNAPPROVED]);
  vi.mocked(createIncentiveAssignments).mockReset().mockResolvedValue(["n1"]);
  vi.mocked(updateIncentiveAssignment).mockReset().mockResolvedValue(undefined);
  vi.mocked(deleteIncentiveAssignment).mockReset().mockResolvedValue(undefined);
  vi.mocked(listIncentiveDefinitions)
    .mockReset()
    .mockResolvedValue([{ id: "d1", code: "SGK_5510_MD81", name: "5510 md.81", kind: "employerSgkRateDiscount", isActive: true, isSmmmApproved: true }]);
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
          <EmployeeIncentivesCard employee={{ id: "e1", displayName: "Ayşe Yılmaz" }} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("EmployeeIncentivesCard", () => {
  it("lists the assignments with declaration code, approval badge, range and note", async () => {
    renderCard();

    const approved = (await screen.findByText("SGK_5510_MD81 - 5510 md.81")).closest("tr")!;
    expect(within(approved).getByText("05510")).toBeInTheDocument();
    expect(within(approved).getByText("SMMM onaylı")).toBeInTheDocument();
    expect(within(approved).getByText("01/2026 - süresiz")).toBeInTheDocument();
    expect(within(approved).getByText("Danışman onayı")).toBeInTheDocument();
    expect(within(approved).queryByTestId("incentive-unapproved-hint")).not.toBeInTheDocument();
    expect(listIncentiveAssignments).toHaveBeenCalledWith({ employeeId: "e1" });
  });

  it("tells that an unapproved definition is not calculated, and tolerates missing optional fields", async () => {
    renderCard();

    const row = (await screen.findByText("ARGE_5746_GV_TASLAK")).closest("tr")!;
    expect(within(row).getByText("Onay bekliyor")).toBeInTheDocument();
    expect(within(row).getByText("Hesaba dahil edilmez, yalnız bildirge kodu yazılır.")).toBeInTheDocument();
    expect(within(row).getByText("03/2026 - 12/2026")).toBeInTheDocument();
  });

  it("is read-only without the manage permission", async () => {
    permissions.manage = false;
    renderCard();

    expect(await screen.findByText("SGK_5510_MD81 - 5510 md.81")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Teşvik ata" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "SGK_5510_MD81 atamasını sil" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /bitiş ayını düzenle/ })).not.toBeInTheDocument();
  });

  it("assigns an incentive to this employee only, without an employee picker", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("button", { name: "Teşvik ata" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).queryByText("Çalışan ekle")).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole("combobox", { name: /Teşvik tanımı/ }));
    await user.click(await screen.findByRole("option", { name: "SGK_5510_MD81 - 5510 md.81", hidden: true }));
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    await waitFor(() => expect(createIncentiveAssignments).toHaveBeenCalledWith(expect.objectContaining({ incentiveDefinitionId: "d1", employeeIds: ["e1"], note: null })));
  });

  it("ends an assignment by editing its end month and keeps the note", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("button", { name: "SGK_5510_MD81 atamasının bitiş ayını düzenle" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText("Bitiş ayı"), "2026-12");
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    await waitFor(() => expect(updateIncentiveAssignment).toHaveBeenCalledWith("a1", { toYear: 2026, toMonth: 12, note: "Danışman onayı" }));
  });

  it("deletes after confirmation", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("button", { name: "SGK_5510_MD81 atamasını sil" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Sil" }));

    await waitFor(() => expect(deleteIncentiveAssignment).toHaveBeenCalledWith("a1"));
  });

  it("shows an empty state", async () => {
    vi.mocked(listIncentiveAssignments).mockResolvedValue([]);
    renderCard();

    expect(await screen.findByTestId("employee-incentives-empty")).toBeInTheDocument();
  });
});

describe("hasEffectiveSgkAssignment", () => {
  const now = new Date(2026, 9, 13);

  it("is true for an SGK-category assignment covering the month, false for tax relief, ended or future ones", () => {
    expect(hasEffectiveSgkAssignment([APPROVED], now)).toBe(true);
    expect(hasEffectiveSgkAssignment([UNAPPROVED], now)).toBe(false);
    expect(hasEffectiveSgkAssignment([{ ...APPROVED, toYear: 2026, toMonth: 9 }], now)).toBe(false);
    expect(hasEffectiveSgkAssignment([{ ...APPROVED, fromYear: 2026, fromMonth: 11 }], now)).toBe(false);
    expect(hasEffectiveSgkAssignment(undefined, now)).toBe(false);
  });
});
