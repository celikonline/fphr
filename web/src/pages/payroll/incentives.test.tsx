import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { MemoryRouter } from "react-router";
import { AxiosError } from "axios";
import trPayroll from "../../../public/locales/tr/payroll.json";
import trCommon from "../../../public/locales/tr/common.json";
import IncentivesPage from "./incentives";
import {
  approveIncentiveDefinition,
  createIncentiveFromTemplate,
  getIncentiveParameterCatalog,
  listIncentiveDefinitions,
  listIncentiveTemplates,
  revokeIncentiveApproval,
  setIncentiveDefinitionActive,
  type IncentiveDefinition,
} from "@/services/payroll-incentives.service";

const permissions = { view: true, manage: true, approve: true };
vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return {
    ...actual,
    usePermission: (key: string) =>
      key === "payroll.incentive.manage" ? permissions.manage : key === "payroll.incentive.approve" ? permissions.approve : key === "payroll.trace.view" ? permissions.view : false,
  };
});

vi.mock("@/services/payroll-incentives.service", () => ({
  listIncentiveDefinitions: vi.fn(),
  listIncentiveTemplates: vi.fn(),
  getIncentiveParameterCatalog: vi.fn(),
  createIncentiveDefinition: vi.fn(async () => "new-id"),
  createIncentiveFromTemplate: vi.fn(async () => "tpl-id"),
  updateIncentiveDefinition: vi.fn(async () => undefined),
  setIncentiveDefinitionActive: vi.fn(async () => undefined),
  approveIncentiveDefinition: vi.fn(async () => undefined),
  revokeIncentiveApproval: vi.fn(async () => undefined),
}));

const APPROVED: IncentiveDefinition = {
  id: "d1",
  code: "SGK_5510_MD81",
  name: "5510 md.81 indirimi",
  lawReference: "5510 s.K. md.81",
  declarationCode: "05510",
  kind: "employerSgkRateDiscount",
  category: "sgk",
  parameters: { rate: { legislationKey: "Payroll.Sgk.EmployerDiscountRate" } },
  isSmmmApproved: true,
  approvedAt: "2026-10-13T09:00:00Z",
  approvedByName: "Ayşe SMMM",
  approvalNote: "Sistem: mevcut 5 puan davranışı",
  isComplete: true,
  isActive: true,
  validFrom: "2026-01-01",
};

const DRAFT: IncentiveDefinition = {
  id: "d2",
  code: "ARGE_5746_GV_TASLAK",
  name: "5746 Ar-Ge GV taslağı",
  lawReference: "5746 s.K.",
  kind: "incomeTaxWithholdingRelief",
  category: "tax",
  isSmmmApproved: false,
  isComplete: false,
  isActive: false,
  validFrom: "2026-10-13",
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
  permissions.view = true;
  permissions.manage = true;
  permissions.approve = true;
  vi.mocked(listIncentiveDefinitions).mockReset().mockResolvedValue([APPROVED, DRAFT]);
  vi.mocked(listIncentiveTemplates)
    .mockReset()
    .mockResolvedValue([
      { templateKey: "TR_5510_MD81", code: "SGK_5510_MD81", name: "5510 md.81 indirimi", kind: "employerSgkRateDiscount", declarationCode: "05510", preApproved: true, alreadyAdded: true },
      { templateKey: "TR_6111", code: "SGK_6111_TASLAK", name: "6111 teşviki", lawReference: "6111 / 4447 geç. md.10", kind: "employerShareCoverage", preApproved: false, alreadyAdded: false },
    ]);
  vi.mocked(getIncentiveParameterCatalog).mockReset().mockResolvedValue({ legislationKeys: [] });
  vi.mocked(createIncentiveFromTemplate).mockReset().mockResolvedValue("tpl-id");
  vi.mocked(setIncentiveDefinitionActive).mockClear();
  vi.mocked(approveIncentiveDefinition).mockReset().mockResolvedValue(undefined);
  vi.mocked(revokeIncentiveApproval).mockReset().mockResolvedValue(undefined);
});

function renderPage() {
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
          <MemoryRouter>
            <IncentivesPage />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

function serverError(status: number, detail: string) {
  return new AxiosError("Bad", "ERR_BAD_REQUEST", undefined, undefined, {
    status,
    statusText: "Error",
    headers: {},
    config: {} as never,
    data: { detail },
  });
}

describe("IncentivesPage", () => {
  it("lists code, name, kind/category, legal basis, declaration code, approval badge and status", async () => {
    renderPage();

    const approved = (await screen.findByText("SGK_5510_MD81")).closest("tr")!;
    expect(within(approved).getByText("5510 md.81 indirimi")).toBeInTheDocument();
    expect(within(approved).getByText("İşveren SGK oran indirimi")).toBeInTheDocument();
    expect(within(approved).getByText("SGK")).toBeInTheDocument();
    expect(within(approved).getByText("5510 s.K. md.81")).toBeInTheDocument();
    expect(within(approved).getByText("05510")).toBeInTheDocument();
    expect(within(approved).getByText("SMMM onaylı")).toBeInTheDocument();
    expect(within(approved).getByText(/Ayşe SMMM/)).toBeInTheDocument();
    expect(within(approved).getByText("Aktif")).toBeInTheDocument();

    const draft = screen.getByText("ARGE_5746_GV_TASLAK").closest("tr")!;
    expect(within(draft).getByText("Gelir vergisi terkini")).toBeInTheDocument();
    expect(within(draft).getByText("Vergi")).toBeInTheDocument();
    expect(within(draft).getByText("Onay bekliyor")).toBeInTheDocument();
    expect(within(draft).getByText("Eksik taslak")).toBeInTheDocument();
    expect(within(draft).getByText("Pasif")).toBeInTheDocument();
  });

  it("tolerates missing optional fields (missing = null)", async () => {
    vi.mocked(listIncentiveDefinitions).mockResolvedValue([{ id: "d3", code: "BOS", name: "Eksik alanlı", kind: "declarationOnly" }]);
    renderPage();

    const row = (await screen.findByText("BOS")).closest("tr")!;
    expect(within(row).getByText("Yalnız bildirge kodu")).toBeInTheDocument();
    expect(within(row).getByText("Onay bekliyor")).toBeInTheDocument();
    expect(within(row).getByText("Aktif")).toBeInTheDocument();
    expect(within(row).queryByText("Eksik taslak")).not.toBeInTheDocument();
  });

  it("shows guidance in the empty state", async () => {
    vi.mocked(listIncentiveDefinitions).mockResolvedValue([]);
    renderPage();

    expect(await screen.findByTestId("incentives-empty")).toHaveTextContent(/Henüz tanımlı bir teşvik yok/);
  });

  it("toggles the active state", async () => {
    renderPage();
    const user = userEvent.setup();

    await screen.findByText("SGK_5510_MD81");
    await user.click(screen.getByRole("switch", { name: /SGK_5510_MD81/ }));
    await waitFor(() => expect(setIncentiveDefinitionActive).toHaveBeenCalledWith("d1", false));
  });

  it("offers approve on unapproved and revoke on approved definitions", async () => {
    renderPage();

    const approved = (await screen.findByText("SGK_5510_MD81")).closest("tr")!;
    const draft = screen.getByText("ARGE_5746_GV_TASLAK").closest("tr")!;
    expect(within(approved).getByRole("button", { name: "Onayı geri al" })).toBeInTheDocument();
    expect(within(approved).queryByRole("button", { name: "Onayla" })).not.toBeInTheDocument();
    expect(within(draft).getByRole("button", { name: "Onayla" })).toBeInTheDocument();
    expect(within(draft).queryByRole("button", { name: "Onayı geri al" })).not.toBeInTheDocument();
  });

  it("approves with a note and the declaration disclaimer", async () => {
    renderPage();
    const user = userEvent.setup();

    const draft = (await screen.findByText("ARGE_5746_GV_TASLAK")).closest("tr")!;
    await user.click(within(draft).getByRole("button", { name: "Onayla" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByTestId("incentive-approval-disclaimer")).toHaveTextContent(/SMMM\/uzman onayı beyanıdır/);
    await user.type(within(dialog).getByLabelText(/^Not/), "Danışman onayladı");
    await user.click(within(dialog).getByRole("button", { name: "Onayla" }));

    await waitFor(() => expect(approveIncentiveDefinition).toHaveBeenCalledWith("d2", "Danışman onayladı"));
  });

  it("shows the server error when the approval is refused (incomplete draft)", async () => {
    vi.mocked(approveIncentiveDefinition).mockRejectedValue(serverError(400, "Teşvik tanımı eksik: 'rate' alanı doldurulmalı."));
    renderPage();
    const user = userEvent.setup();

    const draft = (await screen.findByText("ARGE_5746_GV_TASLAK")).closest("tr")!;
    await user.click(within(draft).getByRole("button", { name: "Onayla" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Onayla" }));

    expect(await within(dialog).findByText("Teşvik tanımı eksik: 'rate' alanı doldurulmalı.")).toBeInTheDocument();
  });

  it("revokes an approval", async () => {
    renderPage();
    const user = userEvent.setup();

    const approved = (await screen.findByText("SGK_5510_MD81")).closest("tr")!;
    await user.click(within(approved).getByRole("button", { name: "Onayı geri al" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Onayı geri al" }));

    await waitFor(() => expect(revokeIncentiveApproval).toHaveBeenCalledWith("d1", ""));
  });

  it("hides the approval buttons without payroll.incentive.approve but keeps the write controls", async () => {
    permissions.approve = false;
    renderPage();

    await screen.findByText("SGK_5510_MD81");
    expect(screen.queryByRole("button", { name: "Onayla" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Onayı geri al" })).not.toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Yeni teşvik" }).length).toBeGreaterThan(0);
    expect(screen.getAllByRole("switch")).toHaveLength(2);
  });

  it("hides every write control without payroll.incentive.manage (read-only with trace.view)", async () => {
    permissions.manage = false;
    permissions.approve = false;
    renderPage();

    await screen.findByText("SGK_5510_MD81");
    expect(screen.queryByRole("button", { name: "Yeni teşvik" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Şablondan ekle" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /düzenle/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("switch")).not.toBeInTheDocument();
  });

  it("hides the empty-state buttons without permission", async () => {
    permissions.manage = false;
    vi.mocked(listIncentiveDefinitions).mockResolvedValue([]);
    renderPage();

    await screen.findByTestId("incentives-empty");
    expect(screen.queryByRole("button", { name: "Yeni teşvik" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Şablondan ekle" })).not.toBeInTheDocument();
  });

  it("is reachable with manage alone and denied without trace.view or manage", async () => {
    permissions.view = false;
    permissions.manage = false;
    renderPage();

    await waitFor(() => expect(screen.queryByText("SGK_5510_MD81")).not.toBeInTheDocument());
    expect(listIncentiveDefinitions).not.toHaveBeenCalled();
  });

  it("lists templates with badges, disables the already added one and adds another", async () => {
    renderPage();
    const user = userEvent.setup();

    await screen.findByText("SGK_5510_MD81");
    await user.click(screen.getAllByRole("button", { name: "Şablondan ekle" })[0]);
    const dialog = await screen.findByRole("dialog");

    const added = await within(dialog).findByTestId("incentive-template-TR_5510_MD81");
    expect(within(added).getByText("Onaylı gelir")).toBeInTheDocument();
    expect(within(added).getByText("Eklendi")).toBeInTheDocument();
    expect(within(added).getByRole("button", { name: /şablonunu ekle/ })).toBeDisabled();

    const fresh = within(dialog).getByTestId("incentive-template-TR_6111");
    expect(within(fresh).queryByText("Eklendi")).not.toBeInTheDocument();
    await user.click(within(fresh).getByRole("button", { name: "6111 teşviki şablonunu ekle" }));

    await waitFor(() => expect(createIncentiveFromTemplate).toHaveBeenCalledWith("TR_6111"));
  });

  it("shows the server message when a template cannot be added", async () => {
    vi.mocked(createIncentiveFromTemplate).mockRejectedValue(serverError(409, "Bu şablon zaten eklenmiş."));
    renderPage();
    const user = userEvent.setup();

    await screen.findByText("SGK_5510_MD81");
    await user.click(screen.getAllByRole("button", { name: "Şablondan ekle" })[0]);
    const dialog = await screen.findByRole("dialog");
    await user.click(await within(dialog).findByRole("button", { name: "6111 teşviki şablonunu ekle" }));

    expect(await within(dialog).findByText("Bu şablon zaten eklenmiş.")).toBeInTheDocument();
  });

  it("opens the create form from the new button", async () => {
    renderPage();
    const user = userEvent.setup();

    await screen.findByText("SGK_5510_MD81");
    await user.click(screen.getAllByRole("button", { name: "Yeni teşvik" })[0]);
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText(/^Kod/)).toBeEnabled();
  });
});
