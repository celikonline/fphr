import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { AxiosError } from "axios";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { MemoryRouter, Route, Routes } from "react-router";
import trPayroll from "../../../public/locales/tr/payroll.json";
import trCommon from "../../../public/locales/tr/common.json";
import PayrollPeriodsPage from "./periods";
import { listPayrollPeriods, openPayrollPeriod } from "@/services/payroll.service";

let canImport = true;
vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return { ...actual, usePermission: (key: string) => (key === "payroll.payslip.import" ? canImport : true) };
});

vi.mock("@/services/payroll.service", () => ({
  listPayrollPeriods: vi.fn(async () => []),
  openPayrollPeriod: vi.fn(async () => "new-period-id"),
}));

const GUIDANCE = /çalışan kartı > Bordro sekmesinde/;

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
  canImport = true;
  vi.mocked(listPayrollPeriods).mockResolvedValue([]);
  vi.mocked(openPayrollPeriod).mockClear();
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
          <MemoryRouter initialEntries={["/app/payroll/periods"]}>
            <Routes>
              <Route path="/app/payroll/periods" element={<PayrollPeriodsPage />} />
              <Route path="/app/payroll/periods/:id" element={<div>detail-page</div>} />
            </Routes>
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("PayrollPeriodsPage empty state", () => {
  it("shows guidance, an open-period action and links when there are no periods", async () => {
    renderPage();

    expect((await screen.findAllByText(GUIDANCE)).length).toBeGreaterThan(0);
    expect(screen.queryByText("Henüz açılmış bir bordro dönemi yok.")).not.toBeInTheDocument();
    for (const link of screen.getAllByRole("link", { name: /Çalışanlar/ })) {
      expect(link).toHaveAttribute("href", "/app/employees");
    }
    for (const link of screen.getAllByRole("link", { name: /Bordro PDF yükle/ })) {
      expect(link).toHaveAttribute("href", "/app/payroll/payslip-import");
    }
  });

  it("hides the payslip import link without payroll.payslip.import", async () => {
    canImport = false;
    renderPage();

    await screen.findAllByText(GUIDANCE);
    expect(screen.queryByRole("link", { name: /Bordro PDF yükle/ })).not.toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: /Çalışanlar/ }).length).toBeGreaterThan(0);
  });

  it("opens the period for the chosen year and month and navigates to its detail", async () => {
    renderPage();
    const user = userEvent.setup();
    const nextYear = new Date().getFullYear() + 1;

    await screen.findAllByText(GUIDANCE);
    await user.click(screen.getAllByRole("button", { name: "Dönem aç" })[0]);

    const dialog = await screen.findByRole("dialog");
    const yearInput = within(dialog).getByLabelText(/Yıl/);
    await user.clear(yearInput);
    await user.type(yearInput, String(nextYear));
    await user.click(within(dialog).getByLabelText(/^Ay/));
    await user.click(await screen.findByRole("option", { name: "Mart", hidden: true }));
    await user.click(within(dialog).getByRole("button", { name: "Dönemi aç" }));

    await waitFor(() => expect(openPayrollPeriod).toHaveBeenCalledWith(nextYear, 3));
    expect(await screen.findByText("detail-page")).toBeInTheDocument();
  });

  it("keeps submit disabled for years outside the API window", async () => {
    renderPage();
    const user = userEvent.setup();
    const now = new Date();

    await screen.findAllByText(GUIDANCE);
    await user.click(screen.getAllByRole("button", { name: "Dönem aç" })[0]);
    const dialog = await screen.findByRole("dialog");
    const yearInput = within(dialog).getByLabelText(/Yıl/);
    const submit = within(dialog).getByRole("button", { name: "Dönemi aç" });

    for (const invalid of ["2023", String(now.getFullYear() + 2)]) {
      await user.clear(yearInput);
      await user.type(yearInput, invalid);
      expect(submit).toBeDisabled();
    }
    await user.clear(yearInput);
    await user.type(yearInput, "2024");
    expect(submit).toBeEnabled();
  });

  it("resets year and month to the current month each time the modal opens", async () => {
    renderPage();
    const user = userEvent.setup();
    const now = new Date();

    await screen.findAllByText(GUIDANCE);
    await user.click(screen.getAllByRole("button", { name: "Dönem aç" })[0]);
    let dialog = await screen.findByRole("dialog");
    const yearInput = within(dialog).getByLabelText(/Yıl/);
    await user.clear(yearInput);
    await user.type(yearInput, "2024");
    await user.click(within(dialog).getByRole("button", { name: "İptal" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

    await user.click(screen.getAllByRole("button", { name: "Dönem aç" })[0]);
    dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByLabelText(/Yıl/)).toHaveValue(String(now.getFullYear()));
  });

  it("opens the period for the current month by default", async () => {
    renderPage();
    const user = userEvent.setup();
    const now = new Date();

    await screen.findAllByText(GUIDANCE);
    await user.click(screen.getAllByRole("button", { name: "Dönem aç" })[0]);
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Dönemi aç" }));

    await waitFor(() => expect(openPayrollPeriod).toHaveBeenCalledWith(now.getFullYear(), now.getMonth() + 1));
  });

  it("keeps the header open-period button when periods exist", async () => {
    vi.mocked(listPayrollPeriods).mockResolvedValue([{ id: "p1", year: 2026, month: 8, status: "open" }]);
    renderPage();

    expect((await screen.findAllByText(/Ağustos 2026/)).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "Dönem aç" })).toBeInTheDocument();
  });

  it("shows the server error inside the modal when opening fails", async () => {
    vi.mocked(openPayrollPeriod).mockRejectedValueOnce(
      new AxiosError("fail", "ERR_BAD_REQUEST", undefined, undefined, {
        status: 409,
        statusText: "Conflict",
        headers: {},
        config: {} as never,
        data: { detail: "Bu dönem zaten açık." },
      }),
    );
    renderPage();
    const user = userEvent.setup();

    await screen.findAllByText(GUIDANCE);
    await user.click(screen.getAllByRole("button", { name: "Dönem aç" })[0]);
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Dönemi aç" }));

    expect(await within(dialog).findByText("Bu dönem zaten açık.")).toBeInTheDocument();
  });
});
