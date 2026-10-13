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
import { IncentiveApproveModal, type IncentiveApprovalMode } from "./incentive-approve-modal";
import { approveIncentiveDefinition, revokeIncentiveApproval, type IncentiveDefinition } from "@/services/payroll-incentives.service";

vi.mock("@/services/payroll-incentives.service", () => ({
  approveIncentiveDefinition: vi.fn(async () => undefined),
  revokeIncentiveApproval: vi.fn(async () => undefined),
}));

const DRAFT: IncentiveDefinition = { id: "d1", code: "SGK_6111_TASLAK", name: "6111 taslağı", kind: "employerShareCoverage", isSmmmApproved: false, isComplete: false };

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
});

beforeEach(() => {
  vi.mocked(approveIncentiveDefinition).mockReset().mockResolvedValue(undefined);
  vi.mocked(revokeIncentiveApproval).mockReset().mockResolvedValue(undefined);
});

function renderModal(mode: IncentiveApprovalMode, item: IncentiveDefinition | null = DRAFT) {
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
          <IncentiveApproveModal opened onClose={onClose} mode={mode} item={item} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
  return { onClose };
}

function serverError(detail: string) {
  return new AxiosError("Bad", "ERR_BAD_REQUEST", undefined, undefined, {
    status: 400,
    statusText: "Bad Request",
    headers: {},
    config: {} as never,
    data: { code: "payroll.errors.incentive_parameters_incomplete", detail },
  });
}

describe("IncentiveApproveModal", () => {
  it("explains that approval is a declaration and sends the note", async () => {
    const { onClose } = renderModal("approve");
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    expect(within(dialog).getByTestId("incentive-approval-disclaimer")).toHaveTextContent(/doğruluğu sizin sorumluluğunuzdadır/);
    expect(within(dialog).getByTestId("incentive-approval-incomplete")).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText(/^Not/), "Danışman görüşü 2026/14");
    await user.click(within(dialog).getByRole("button", { name: "Onayla" }));

    await waitFor(() => expect(approveIncentiveDefinition).toHaveBeenCalledWith("d1", "Danışman görüşü 2026/14"));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("shows the server's completeness / key error and stays open", async () => {
    vi.mocked(approveIncentiveDefinition).mockRejectedValue(serverError("Teşvik tanımı eksik: 'rate' alanı doldurulmalı."));
    const { onClose } = renderModal("approve");
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("button", { name: "Onayla" }));

    expect(await within(dialog).findByText("Teşvik tanımı eksik: 'rate' alanı doldurulmalı.")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("revokes the approval with an optional note and no disclaimer", async () => {
    const { onClose } = renderModal("revoke", { ...DRAFT, isSmmmApproved: true, isComplete: true });
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    expect(within(dialog).queryByTestId("incentive-approval-disclaimer")).not.toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: "Onayı geri al" }));

    await waitFor(() => expect(revokeIncentiveApproval).toHaveBeenCalledWith("d1", ""));
    expect(approveIncentiveDefinition).not.toHaveBeenCalled();
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("shows the server error when revoking fails", async () => {
    vi.mocked(revokeIncentiveApproval).mockRejectedValue(serverError("Teşvik tanımı bulunamadı."));
    renderModal("revoke", { ...DRAFT, isSmmmApproved: true });
    const user = userEvent.setup();
    const dialog = await screen.findByRole("dialog");

    await user.click(within(dialog).getByRole("button", { name: "Onayı geri al" }));

    expect(await within(dialog).findByText("Teşvik tanımı bulunamadı.")).toBeInTheDocument();
  });
});
