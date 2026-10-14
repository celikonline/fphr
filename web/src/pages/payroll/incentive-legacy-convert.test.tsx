import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trPayroll from "../../../public/locales/tr/payroll.json";
import trCommon from "../../../public/locales/tr/common.json";
import { IncentiveLegacyConvert, suggestStartMonth } from "./incentive-legacy-convert";
import { toast } from "@/hooks/use-toast";
import { listPayrollPeriods } from "@/services/payroll.service";
import { convertLegacyProfileCode, listIncentiveDefinitions, listLegacyProfileCodes, type LegacyProfileCode } from "@/services/payroll-incentives.service";

vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));
vi.mock("@/services/payroll.service", () => ({ listPayrollPeriods: vi.fn() }));
vi.mock("@/services/payroll-incentives.service", () => ({
  listLegacyProfileCodes: vi.fn(),
  convertLegacyProfileCode: vi.fn(),
  listIncentiveDefinitions: vi.fn(),
}));

const SAFE: LegacyProfileCode = {
  code: "5510",
  normalizedCode: "05510",
  employeeCount: 4,
  suggestedDefinitionId: "d1",
  suggestedDefinitionCode: "SGK_5510_MD81",
  suggestedIsApproved: true,
  calculationChanges: false,
};
const SAFE_ZERO: LegacyProfileCode = { ...SAFE, code: "05510", employeeCount: 2 };
const RISKY: LegacyProfileCode = {
  code: "6111",
  normalizedCode: "06111",
  employeeCount: 3,
  suggestedDefinitionId: "d2",
  suggestedDefinitionCode: "SGK_6111_TASLAK",
  suggestedIsApproved: false,
  calculationChanges: true,
};
const NO_MATCH: LegacyProfileCode = { code: "XYZ", normalizedCode: "", employeeCount: 1 };

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
  vi.mocked(toast).mockReset();
  vi.mocked(listLegacyProfileCodes).mockReset().mockResolvedValue([SAFE, SAFE_ZERO, RISKY, NO_MATCH]);
  vi.mocked(convertLegacyProfileCode).mockReset().mockResolvedValue({ createdIds: ["n1", "n2", "n3"], skippedEmployeeIds: ["s1"] });
  vi.mocked(listPayrollPeriods)
    .mockReset()
    .mockResolvedValue([
      { id: "p8", year: 2026, month: 8, status: "locked" },
      { id: "p10", year: 2026, month: 10, status: "open" },
      { id: "p9", year: 2026, month: 9, status: "open" },
    ]);
  vi.mocked(listIncentiveDefinitions)
    .mockReset()
    .mockResolvedValue([
      { id: "d1", code: "SGK_5510_MD81", name: "5510 md.81", kind: "employerSgkRateDiscount", declarationCode: "05510", parameters: { rate: { legislationKey: "Payroll.Sgk.EmployerDiscountRate" } }, isActive: true, isSmmmApproved: true, validFrom: "2026-01-01" },
      { id: "d2", code: "SGK_6111_TASLAK", name: "6111 taslak", kind: "employerShareCoverage", isActive: true, isSmmmApproved: false, validFrom: "2026-01-01" },
      { id: "d4", code: "ARGE", name: "Ar-Ge", kind: "incomeTaxWithholdingRelief", isActive: true, isSmmmApproved: true },
    ]);
});

function renderTab() {
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
          <IncentiveLegacyConvert />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

async function openConvert(user: ReturnType<typeof userEvent.setup>, code: string) {
  const row = await screen.findByTestId(`legacy-row-${code}`);
  await user.click(within(row).getByRole("button", { name: "Dönüştür" }));
  return screen.findByRole("dialog");
}

describe("IncentiveLegacyConvert", () => {
  it("lists the codes with normalized code, count, suggestion and the effect on the calculation", async () => {
    renderTab();

    const safe = await screen.findByTestId("legacy-row-5510");
    expect(within(safe).getByText("05510")).toBeInTheDocument();
    expect(within(safe).getByText("4")).toBeInTheDocument();
    expect(within(safe).getByText("SGK_5510_MD81")).toBeInTheDocument();
    expect(within(safe).getByText("Hesap değişmez")).toBeInTheDocument();
    const risky = screen.getByTestId("legacy-row-6111");
    expect(within(risky).getByText(trPayroll.incentiveLegacy.calculationChanges)).toBeInTheDocument();
    expect(within(screen.getByTestId("legacy-row-XYZ")).getByText("Eşleşen tanım yok")).toBeInTheDocument();
  });

  it("explains that 5510 and 05510 are converted together", async () => {
    renderTab();

    expect(await screen.findByTestId("legacy-grouping-note")).toHaveTextContent('"5510" ve "05510"');
    expect(within(await screen.findByTestId("legacy-row-5510")).getByText('"05510" olarak birlikte dönüşür')).toBeInTheDocument();
    expect(within(screen.getByTestId("legacy-row-05510")).getByText('"05510" olarak birlikte dönüşür')).toBeInTheDocument();
    expect(within(screen.getByTestId("legacy-row-6111")).queryByText(/birlikte dönüşür/)).not.toBeInTheDocument();
  });

  it("converts without a red warning when the calculation stays the same, suggesting the first open month, and reports the result", async () => {
    const user = userEvent.setup();
    renderTab();

    const dialog = await openConvert(user, "5510");
    expect(within(dialog).queryByTestId("legacy-calculation-warning")).not.toBeInTheDocument();
    await waitFor(() => expect(within(dialog).getByLabelText(/Başlangıç ayı/)).toHaveValue("2026-09"));
    await user.click(within(dialog).getByRole("button", { name: "Dönüştür" }));

    await waitFor(() =>
      expect(convertLegacyProfileCode).toHaveBeenCalledWith({
        code: "5510",
        incentiveDefinitionId: "d1",
        fromYear: 2026,
        fromMonth: 9,
        toYear: null,
        toMonth: null,
      }),
    );
    await waitFor(() => expect(toast).toHaveBeenCalledWith(expect.objectContaining({ description: "3 atama oluşturuldu, 1 çalışan atlandı (çakışan SGK ataması)." })));
  });

  it("shows a red warning when the calculation changes and requires the confirmation box", async () => {
    const user = userEvent.setup();
    renderTab();

    const dialog = await openConvert(user, "6111");
    const warning = await within(dialog).findByTestId("legacy-calculation-warning");
    expect(warning).toHaveTextContent("Seçilen model işveren maliyetini değiştirir");
    const submit = within(dialog).getByRole("button", { name: "Dönüştür" });
    await waitFor(() => expect(within(dialog).getByLabelText(/Başlangıç ayı/)).toHaveValue("2026-09"));
    expect(submit).toBeDisabled();

    await user.click(within(dialog).getByRole("checkbox", { name: "Hesabın değişeceğini anladım" }));
    expect(submit).toBeEnabled();
    await user.click(submit);

    await waitFor(() => expect(convertLegacyProfileCode).toHaveBeenCalledWith(expect.objectContaining({ code: "6111", incentiveDefinitionId: "d2", fromYear: 2026, fromMonth: 9 })));
  });

  it("falls back to the current month when no period is open and lets the user pick a definition for an unmatched code", async () => {
    vi.mocked(listPayrollPeriods).mockResolvedValue([{ id: "p8", year: 2026, month: 8, status: "locked" }]);
    const user = userEvent.setup();
    renderTab();

    const dialog = await openConvert(user, "XYZ");
    await user.click(within(dialog).getByRole("combobox", { name: /Teşvik tanımı/ }));
    expect(screen.queryByRole("option", { name: /ARGE/, hidden: true })).not.toBeInTheDocument();
    await user.click(await screen.findByRole("option", { name: "SGK_5510_MD81 - 5510 md.81", hidden: true }));

    const now = new Date();
    const expected = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}`;
    await waitFor(() => expect(within(dialog).getByLabelText(/Başlangıç ayı/)).toHaveValue(expected));
    expect(within(dialog).getByTestId("legacy-calculation-warning")).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Dönüştür" })).toBeDisabled();
  });

  it("shows an empty state", async () => {
    vi.mocked(listLegacyProfileCodes).mockResolvedValue([]);
    renderTab();

    expect(await screen.findByTestId("legacy-empty")).toBeInTheDocument();
  });
});

describe("suggestStartMonth", () => {
  it("returns the earliest open period or the current month", () => {
    const today = new Date(2026, 9, 13);
    expect(
      suggestStartMonth(
        [
          { year: 2026, month: 10, status: "open" },
          { year: 2026, month: 9, status: "open" },
        ],
        today,
      ),
    ).toBe("2026-09");
    expect(suggestStartMonth([{ year: 2026, month: 9, status: "locked" }], today)).toBe("2026-10");
  });
});
