import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { MemoryRouter } from "react-router";
import trPayroll from "../../../public/locales/tr/payroll.json";
import trCommon from "../../../public/locales/tr/common.json";
import PayItemsPage from "./pay-items";
import {
  createPayItem,
  getFormulaCatalog,
  listPayItems,
  setPayItemActive,
  updatePayItem,
  type PayItemDefinition,
} from "@/services/payroll-pay-items.service";

const permissions = { view: true, manage: true };
vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return {
    ...actual,
    usePermission: (key: string) => (key === "payroll.payitem.manage" ? permissions.manage : key === "payroll.trace.view" ? permissions.view : false),
  };
});

vi.mock("@/services/payroll-pay-items.service", () => ({
  listPayItems: vi.fn(),
  createPayItem: vi.fn(async () => "new-id"),
  updatePayItem: vi.fn(async () => undefined),
  setPayItemActive: vi.fn(async () => undefined),
  getFormulaCatalog: vi.fn(),
  validatePayItemFormula: vi.fn(),
}));

const FULL: PayItemDefinition = {
  id: "i1",
  code: "YEMEK",
  name: "Yemek yardımı",
  direction: "earning",
  amountBasis: "gross",
  isSgkSubject: false,
  isIncomeTaxSubject: true,
  isStampTaxSubject: true,
  formula: "deger",
  defaultValue: 100,
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
  permissions.view = true;
  permissions.manage = true;
  vi.mocked(listPayItems).mockReset().mockResolvedValue([FULL]);
  vi.mocked(createPayItem).mockClear();
  vi.mocked(updatePayItem).mockClear();
  vi.mocked(setPayItemActive).mockClear();
  vi.mocked(getFormulaCatalog).mockReset().mockResolvedValue({ variables: [{ name: "brut", sampleValue: 20000 }], functions: [{ name: "min", aliases: [], minArgs: 2 }] });
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
            <PayItemsPage />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("PayItemsPage", () => {
  it("lists code, name, amount type, tax flags and status", async () => {
    renderPage();

    const row = (await screen.findByText("YEMEK")).closest("tr")!;
    expect(within(row).getByText("Yemek yardımı")).toBeInTheDocument();
    expect(within(row).getByText("Brüt")).toBeInTheDocument();
    expect(within(row).getByText("GV · DV")).toBeInTheDocument();
    expect(within(row).getByText("Aktif")).toBeInTheDocument();
  });

  it("tolerates missing optional fields (missing = null)", async () => {
    vi.mocked(listPayItems).mockResolvedValue([{ id: "i2", code: "BOS", name: "Eksik alanlı", direction: "earning", amountBasis: "net" }]);
    renderPage();

    const row = (await screen.findByText("BOS")).closest("tr")!;
    expect(within(row).getByText("Net")).toBeInTheDocument();
    expect(within(row).getByText("SGK · GV · DV")).toBeInTheDocument();
    expect(within(row).getByText("Aktif")).toBeInTheDocument();
  });

  it("shows guidance in the empty state", async () => {
    vi.mocked(listPayItems).mockResolvedValue([]);
    renderPage();

    expect(await screen.findByTestId("pay-items-empty")).toHaveTextContent(/Henüz tanımlı bir ödeme türü yok/);
  });

  it("creates a pay item through the form", async () => {
    renderPage();
    const user = userEvent.setup();

    await screen.findByText("YEMEK");
    await user.click(screen.getAllByRole("button", { name: "Yeni ödeme türü" })[0]);
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText(/^Kod/), "ikramiye");
    await user.type(within(dialog).getByLabelText(/^Ad/), "İkramiye");
    await user.type(within(dialog).getByLabelText(/^Formül/), "brut * 0.1");
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    await waitFor(() => expect(createPayItem).toHaveBeenCalledTimes(1));
    expect(vi.mocked(createPayItem).mock.calls[0][0]).toMatchObject({
      code: "IKRAMIYE",
      name: "İkramiye",
      direction: "earning",
      amountBasis: "gross",
      isSgkSubject: true,
      formula: "brut * 0.1",
    });
  });

  it("locks the code when editing and saves the update without it", async () => {
    renderPage();
    const user = userEvent.setup();

    await screen.findByText("YEMEK");
    await user.click(screen.getByRole("button", { name: "YEMEK kodlu ödeme türünü düzenle" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText(/^Kod/)).toBeDisabled();
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    await waitFor(() => expect(updatePayItem).toHaveBeenCalledTimes(1));
    expect(vi.mocked(updatePayItem).mock.calls[0][0]).toBe("i1");
    expect(vi.mocked(updatePayItem).mock.calls[0][1]).not.toHaveProperty("code");
  });

  it("forces and locks the tax flags when Net is chosen", async () => {
    vi.mocked(listPayItems).mockResolvedValue([]);
    renderPage();
    const user = userEvent.setup();

    await user.click((await screen.findAllByRole("button", { name: "Yeni ödeme türü" }))[0]);
    const dialog = await screen.findByRole("dialog");
    const sgk = within(dialog).getByLabelText("SGK primine tabi");
    await user.click(sgk);
    expect(sgk).not.toBeChecked();

    await user.click(within(dialog).getByText("Net"));

    for (const label of ["SGK primine tabi", "Gelir vergisine tabi", "Damga vergisine tabi"]) {
      const box = within(dialog).getByLabelText(label);
      expect(box).toBeChecked();
      expect(box).toBeDisabled();
    }
  });

  it("only offers the earning direction", async () => {
    vi.mocked(listPayItems).mockResolvedValue([]);
    renderPage();
    const user = userEvent.setup();

    await user.click((await screen.findAllByRole("button", { name: "Yeni ödeme türü" }))[0]);
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/Şimdilik yalnız kazanç tanımlanabilir/)).toBeInTheDocument();
  });

  it("toggles active state", async () => {
    renderPage();
    const user = userEvent.setup();

    await screen.findByText("YEMEK");
    await user.click(screen.getByRole("switch", { name: /YEMEK/ }));
    await waitFor(() => expect(setPayItemActive).toHaveBeenCalledWith("i1", false));
  });

  it("hides every write control without payroll.payitem.manage", async () => {
    permissions.manage = false;
    renderPage();

    await screen.findByText("YEMEK");
    expect(screen.queryByRole("button", { name: "Yeni ödeme türü" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /düzenle/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("switch")).not.toBeInTheDocument();
  });

  it("hides the empty-state create button without permission", async () => {
    permissions.manage = false;
    vi.mocked(listPayItems).mockResolvedValue([]);
    renderPage();

    await screen.findByTestId("pay-items-empty");
    expect(screen.queryByRole("button", { name: "Yeni ödeme türü" })).not.toBeInTheDocument();
  });
});
