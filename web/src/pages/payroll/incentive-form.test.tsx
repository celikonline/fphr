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
import { IncentiveForm } from "./incentive-form";
import {
  createIncentiveDefinition,
  getIncentiveParameterCatalog,
  updateIncentiveDefinition,
  type IncentiveDefinition,
} from "@/services/payroll-incentives.service";

vi.mock("@/services/payroll-incentives.service", () => ({
  createIncentiveDefinition: vi.fn(async () => "new-id"),
  updateIncentiveDefinition: vi.fn(async () => undefined),
  getIncentiveParameterCatalog: vi.fn(),
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
  isComplete: true,
  isActive: true,
  validFrom: "2026-01-01",
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
  vi.mocked(createIncentiveDefinition).mockReset().mockResolvedValue("new-id");
  vi.mocked(updateIncentiveDefinition).mockReset().mockResolvedValue(undefined);
  vi.mocked(getIncentiveParameterCatalog)
    .mockReset()
    .mockResolvedValue({
      legislationKeys: [
        { key: "Payroll.Sgk.EmployerDiscountRate", value: 0.05, effectiveFrom: "2026-01-01" },
        { key: "Payroll.Sgk.EmployerRate", value: 0.205, effectiveFrom: "2026-01-01" },
      ],
    });
});

function renderForm(item: IncentiveDefinition | null = null) {
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
  const onClose = vi.fn();
  render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <IncentiveForm opened onClose={onClose} item={item} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
  return { onClose };
}

async function chooseKind(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement, label: string) {
  await user.click(within(dialog).getByLabelText(/^Teşvik türü/));
  await user.click(await screen.findByRole("option", { name: label, hidden: true }));
}

describe("IncentiveForm", () => {
  it("opens the fields of the chosen kind", async () => {
    renderForm();
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    // (a) a rate only
    expect(within(dialog).getByLabelText("İndirim oranı")).toBeInTheDocument();
    expect(within(dialog).queryByText("Karşılama tabanı")).not.toBeInTheDocument();

    // (b) rate (share), coverage base, covered premiums
    await chooseKind(user, dialog, "Prim karşılama");
    expect(within(dialog).getByLabelText("Karşılama payı")).toBeInTheDocument();
    expect(within(dialog).getByLabelText(/^Karşılama tabanı/)).toBeInTheDocument();
    expect(within(dialog).getByLabelText("İşveren SGK")).toBeInTheDocument();
    expect(within(dialog).getByLabelText("İşçi işsizlik")).toBeInTheDocument();
    expect(within(dialog).queryByLabelText(/Asgari ücret çarpanı/)).not.toBeInTheDocument();
    expect(within(dialog).queryByLabelText("İşveren SGK oranı")).not.toBeInTheDocument();

    // the multiplier only exists for the minimum-wage base, the employer rate only when the employer SGK is covered
    await user.click(within(dialog).getByLabelText(/^Karşılama tabanı/));
    await user.click(await screen.findByRole("option", { name: "Asgari ücret (tavanlı)", hidden: true }));
    expect(within(dialog).getByLabelText(/Asgari ücret çarpanı/)).toBeInTheDocument();
    await user.click(within(dialog).getByLabelText("İşveren SGK"));
    expect(within(dialog).getByLabelText("İşveren SGK oranı")).toBeInTheDocument();

    // (c) a rate only again
    await chooseKind(user, dialog, "Gelir vergisi terkini");
    expect(within(dialog).getByLabelText("Gelir vergisi teşvik payı")).toBeInTheDocument();
    expect(within(dialog).queryByText("Karşılama tabanı")).not.toBeInTheDocument();

    // (d) no calculation parameters
    await chooseKind(user, dialog, "Yalnız bildirge kodu");
    expect(within(dialog).getByText(/hesaplama parametresi yoktur/)).toBeInTheDocument();
    expect(within(dialog).queryByLabelText("Gelir vergisi teşvik payı")).not.toBeInTheDocument();
    expect(within(dialog).queryByLabelText("İndirim oranı")).not.toBeInTheDocument();
  });

  it("creates a rate discount with a literal percent sent as a fraction", async () => {
    renderForm();
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    await user.type(within(dialog).getByLabelText(/^Kod/), "sgk_test");
    await user.type(within(dialog).getByLabelText(/^Ad/), "Deneme teşviki");
    await user.type(within(dialog).getByLabelText(/^Bildirge/), "06111");
    await user.type(within(dialog).getByLabelText("İndirim oranı"), "5");
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    await waitFor(() => expect(createIncentiveDefinition).toHaveBeenCalledTimes(1));
    expect(vi.mocked(createIncentiveDefinition).mock.calls[0][0]).toMatchObject({
      code: "SGK_TEST",
      name: "Deneme teşviki",
      declarationCode: "06111",
      kind: "employerSgkRateDiscount",
      parameters: { rate: { value: 0.05 } },
    });
  });

  it("sends the coverage parameters of a premium-coverage incentive", async () => {
    renderForm();
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    await user.type(within(dialog).getByLabelText(/^Kod/), "kapsam");
    await user.type(within(dialog).getByLabelText(/^Ad/), "Karşılama");
    await chooseKind(user, dialog, "Prim karşılama");
    await user.type(within(dialog).getByLabelText("Karşılama payı"), "100");
    await user.click(within(dialog).getByLabelText(/^Karşılama tabanı/));
    await user.click(await screen.findByRole("option", { name: "Asgari ücret (tavanlı)", hidden: true }));
    await user.type(within(dialog).getByLabelText(/Asgari ücret çarpanı/), "1");
    await user.click(within(dialog).getByLabelText("İşveren SGK"));
    await user.type(within(dialog).getByLabelText("İşveren SGK oranı"), "15.5");
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    await waitFor(() => expect(createIncentiveDefinition).toHaveBeenCalledTimes(1));
    expect(vi.mocked(createIncentiveDefinition).mock.calls[0][0]).toMatchObject({
      kind: "employerShareCoverage",
      parameters: {
        rate: { value: 1 },
        coverageBase: "minimumWage",
        capMultiplier: 1,
        employerSgkRate: { value: 0.155 },
        coversEmployerSgk: true,
        coversEmployerUnemployment: false,
      },
    });
  });

  it("can reference a legislation key and shows its value in force today", async () => {
    renderForm();
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    await user.type(within(dialog).getByLabelText(/^Kod/), "anahtar");
    await user.type(within(dialog).getByLabelText(/^Ad/), "Anahtarlı");
    await user.click(within(dialog).getByText("Mevzuat parametresi"));
    await user.click(within(dialog).getByLabelText("İndirim oranı"));
    await user.click(await screen.findByRole("option", { name: "Payroll.Sgk.EmployerDiscountRate", hidden: true }));

    expect(await within(dialog).findByTestId("incentive-key-value")).toHaveTextContent("Güncel değer: %5");
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    await waitFor(() => expect(createIncentiveDefinition).toHaveBeenCalledTimes(1));
    expect(vi.mocked(createIncentiveDefinition).mock.calls[0][0]).toMatchObject({
      parameters: { rate: { legislationKey: "Payroll.Sgk.EmployerDiscountRate" } },
    });
  });

  it("saves an incomplete draft (completeness is only checked on approval)", async () => {
    renderForm();
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    await user.type(within(dialog).getByLabelText(/^Kod/), "taslak");
    await user.type(within(dialog).getByLabelText(/^Ad/), "Taslak");
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    await waitFor(() => expect(createIncentiveDefinition).toHaveBeenCalledTimes(1));
    const body = vi.mocked(createIncentiveDefinition).mock.calls[0][0];
    expect(body.parameters?.rate ?? null).toBeNull();
    expect(body.declarationCode).toBeNull();
  });

  it("validates the declaration code as digits only", async () => {
    renderForm();
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    await user.type(within(dialog).getByLabelText(/^Kod/), "kod");
    await user.type(within(dialog).getByLabelText(/^Ad/), "Ad");
    await user.type(within(dialog).getByLabelText(/^Bildirge/), "05A10");

    expect(within(dialog).getByText(/yalnız rakamlardan oluşmalı/)).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Kaydet" })).toBeDisabled();
  });

  it("locks the code when editing and saves the update without it", async () => {
    renderForm(APPROVED);
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    expect(within(dialog).getByLabelText(/^Kod/)).toBeDisabled();
    expect(within(dialog).getByLabelText(/^Kod/)).toHaveValue("SGK_5510_MD81");
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    await waitFor(() => expect(updateIncentiveDefinition).toHaveBeenCalledTimes(1));
    expect(vi.mocked(updateIncentiveDefinition).mock.calls[0][0]).toBe("d1");
    expect(vi.mocked(updateIncentiveDefinition).mock.calls[0][1]).not.toHaveProperty("code");
    expect(vi.mocked(updateIncentiveDefinition).mock.calls[0][1]).toMatchObject({
      kind: "employerSgkRateDiscount",
      parameters: { rate: { legislationKey: "Payroll.Sgk.EmployerDiscountRate" } },
      validFrom: "2026-01-01",
    });
  });

  it("warns that the approval will drop only when a calculation field changes", async () => {
    renderForm(APPROVED);
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    // the name is not a calculation field: the approval stays
    await user.type(within(dialog).getByLabelText(/^Ad/), " (yeni)");
    expect(within(dialog).queryByTestId("incentive-approval-drop-warning")).not.toBeInTheDocument();

    // the declaration code is
    await user.type(within(dialog).getByLabelText(/^Bildirge/), "1");
    expect(await within(dialog).findByTestId("incentive-approval-drop-warning")).toHaveTextContent(/onay düşecek/);

    // changing it back removes the warning again
    await user.type(within(dialog).getByLabelText(/^Bildirge/), "{Backspace}");
    expect(within(dialog).queryByTestId("incentive-approval-drop-warning")).not.toBeInTheDocument();
  });

  it("does not warn for a definition that is not approved", async () => {
    renderForm({ ...APPROVED, isSmmmApproved: false });
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    await user.type(within(dialog).getByLabelText(/^Bildirge/), "1");
    expect(within(dialog).queryByTestId("incentive-approval-drop-warning")).not.toBeInTheDocument();
  });

  it("shows the server message when saving fails (kind/category lock)", async () => {
    const detail = "Atamaları olan teşvikin kategorisi değiştirilemez.";
    vi.mocked(updateIncentiveDefinition).mockRejectedValue(
      new AxiosError("Conflict", "ERR_BAD_REQUEST", undefined, undefined, {
        status: 409,
        statusText: "Conflict",
        headers: {},
        config: {} as never,
        data: { code: "payroll.errors.incentive_kind_category_locked", detail },
      }),
    );
    renderForm(APPROVED);
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    await chooseKind(user, dialog, "Gelir vergisi terkini");
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    expect(await within(dialog).findByText(detail)).toBeInTheDocument();
  });

  it("tolerates a definition with missing optional fields", async () => {
    renderForm({ id: "d2", code: "BOS", name: "Eksik alanlı", kind: "declarationOnly" });
    const dialog = await screen.findByRole("dialog");

    expect(within(dialog).getByLabelText(/^Ad/)).toHaveValue("Eksik alanlı");
    expect(within(dialog).getByLabelText(/^Bildirge/)).toHaveValue("");
    expect(within(dialog).queryByTestId("incentive-approval-drop-warning")).not.toBeInTheDocument();
  });
});
