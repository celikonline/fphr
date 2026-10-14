import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trPayroll from "../../../public/locales/tr/payroll.json";
import trCommon from "../../../public/locales/tr/common.json";
import { PeriodIncentives } from "./period-incentives";
import { listIncentiveAssignments } from "@/services/payroll-incentives.service";
import type { PayrollPeriodStatus } from "@/services/payroll.service";

const permissions = { view: true, manage: true };
vi.mock("@/components/permission-guard", () => ({
  usePermission: (key: string) => key === "payroll.incentive.manage" ? permissions.manage : key === "payroll.trace.view" && permissions.view,
}));
vi.mock("@/services/payroll-incentives.service", () => ({ listIncentiveAssignments: vi.fn() }));

beforeAll(() => {
  window.matchMedia = () => ({ matches: false, media: "", onchange: null, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {}, dispatchEvent: () => false });
  window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
});
beforeEach(() => {
  permissions.view = true;
  permissions.manage = true;
  vi.mocked(listIncentiveAssignments).mockReset().mockResolvedValue([{
    id: "a1", employeeId: "e1", employeeName: "Ayşe Yılmaz", incentiveDefinitionId: "d1", code: "SGK_6111", name: "6111 istihdam teşviki",
    declarationCode: "06111", kind: "employerShareCoverage", isSmmmApproved: true, fromYear: 2026, fromMonth: 10, toYear: 2027, toMonth: 9,
  }]);
});

function renderCard(status: PayrollPeriodStatus = "open") {
  const i18n = i18next.createInstance();
  void i18n.init({ lng: "tr", resources: { tr: { payroll: trPayroll, common: trCommon } }, interpolation: { escapeValue: false }, react: { useSuspense: false } });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(<I18nextProvider i18n={i18n}><QueryClientProvider client={client}><MantineProvider>
    <PeriodIncentives period={{ id: "period-1", year: 2026, month: 10, status }} />
  </MantineProvider></QueryClientProvider></I18nextProvider>);
}

describe("PeriodIncentives", () => {
  it("shows the selected period's employee models, declaration codes and approval state", async () => {
    renderCard();
    const row = (await screen.findByText("Ayşe Yılmaz")).closest("tr")!;
    expect(within(row).getByText("06111")).toBeInTheDocument();
    expect(within(row).getByText("SGK_6111 — 6111 istihdam teşviki")).toBeInTheDocument();
    expect(within(row).getByText(trPayroll.incentives.approval.approved)).toBeInTheDocument();
    expect(listIncentiveAssignments).toHaveBeenCalledWith({ periodId: "period-1" });
  });

  it("keeps locked periods read-only even for a manager", async () => {
    renderCard("locked");
    await screen.findByText("Ayşe Yılmaz");
    expect(screen.queryByRole("button", { name: "Teşvik ata" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /SGK_6111 atamas/ })).not.toBeInTheDocument();
  });

  it("does not fetch incentive data without read or management permission", () => {
    permissions.view = false;
    permissions.manage = false;
    renderCard();
    expect(listIncentiveAssignments).not.toHaveBeenCalled();
    expect(screen.queryByText("Teşvik atamaları")).not.toBeInTheDocument();
  });
});
