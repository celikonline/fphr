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
import { IncentiveAssignModal } from "./incentive-assign-modal";
import { createIncentiveAssignments, listIncentiveDefinitions } from "@/services/payroll-incentives.service";

vi.mock("@/components/employees/employee-picker", () => ({
  default: ({ onChange }: { onChange: (value: { id: string; displayName: string } | null) => void }) => (
    <>
      <button type="button" onClick={() => onChange({ id: "e9", displayName: "Zeynep Kaya" })}>
        pick-employee
      </button>
      <button type="button" onClick={() => onChange({ id: "e10", displayName: "Can Demir" })}>
        pick-employee-2
      </button>
    </>
  ),
}));

vi.mock("@/services/payroll-incentives.service", () => ({
  listIncentiveDefinitions: vi.fn(),
  createIncentiveAssignments: vi.fn(async () => ["n1", "n2"]),
  updateIncentiveAssignment: vi.fn(async () => undefined),
  deleteIncentiveAssignment: vi.fn(async () => undefined),
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
  vi.mocked(createIncentiveAssignments).mockReset().mockResolvedValue(["n1", "n2"]);
  vi.mocked(listIncentiveDefinitions)
    .mockReset()
    .mockResolvedValue([
      { id: "d1", code: "SGK_5510_MD81", name: "5510 md.81", kind: "employerSgkRateDiscount", isActive: true, isSmmmApproved: true, validFrom: "2026-01-01" },
      { id: "d2", code: "SGK_6111_TASLAK", name: "6111 taslak", kind: "employerShareCoverage", isActive: true, isSmmmApproved: false, maxMonths: 12, validFrom: "2026-10-13" },
      { id: "d3", code: "PASIF", name: "Pasif", kind: "declarationOnly", isActive: false },
    ]);
});

function renderModal() {
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
          <IncentiveAssignModal opened onClose={onClose} defaultYear={2026} defaultMonth={10} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
  return { onClose };
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

async function pickDefinition(user: ReturnType<typeof userEvent.setup>, label: string) {
  const dialog = await screen.findByRole("dialog");
  await user.click(within(dialog).getByRole("combobox", { name: /Teşvik tanımı/ }));
  await user.click(await screen.findByRole("option", { name: label, hidden: true }));
  return dialog;
}

describe("IncentiveAssignModal", () => {
  it("assigns one definition to several employees at once", async () => {
    const user = userEvent.setup();
    const { onClose } = renderModal();

    const dialog = await pickDefinition(user, "SGK_5510_MD81 - 5510 md.81");
    await user.click(within(dialog).getByRole("button", { name: "pick-employee" }));
    await user.click(within(dialog).getByRole("button", { name: "pick-employee-2" }));
    expect(within(dialog).getByText("2 / 500 çalışan seçildi")).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText("Not"), "Danışman onaylı");
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    await waitFor(() =>
      expect(createIncentiveAssignments).toHaveBeenCalledWith({
        incentiveDefinitionId: "d1",
        employeeIds: ["e9", "e10"],
        fromYear: 2026,
        fromMonth: 10,
        toYear: null,
        toMonth: null,
        note: "Danışman onaylı",
      }),
    );
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("offers only active definitions", async () => {
    const user = userEvent.setup();
    renderModal();

    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("combobox", { name: /Teşvik tanımı/ }));
    expect(await screen.findByRole("option", { name: "SGK_5510_MD81 - 5510 md.81", hidden: true })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: /PASIF/, hidden: true })).not.toBeInTheDocument();
  });

  it("warns when the picked definition is not SMMM approved (declared only, not calculated)", async () => {
    const user = userEvent.setup();
    renderModal();

    const dialog = await pickDefinition(user, "SGK_6111_TASLAK - 6111 taslak");

    expect(within(dialog).getByTestId("incentive-unapproved-warning")).toHaveTextContent("hesaba dahil edilmez");
  });

  it("does not warn for an approved definition", async () => {
    const user = userEvent.setup();
    renderModal();

    const dialog = await pickDefinition(user, "SGK_5510_MD81 - 5510 md.81");

    expect(within(dialog).queryByTestId("incentive-unapproved-warning")).not.toBeInTheDocument();
    expect(within(dialog).queryByTestId("incentive-validfrom-warning")).not.toBeInTheDocument();
  });

  it("requires an end month when the definition has a MaxMonths limit, and warns when it starts later than the chosen month", async () => {
    const user = userEvent.setup();
    renderModal();

    const dialog = await pickDefinition(user, "SGK_6111_TASLAK - 6111 taslak");
    await user.click(within(dialog).getByRole("button", { name: "pick-employee" }));

    expect(within(dialog).getByText("Bu tanım en çok 12 ay uygulanır; bitiş ayı zorunludur.")).toBeInTheDocument();
    expect(within(dialog).getByTestId("incentive-validfrom-warning")).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Kaydet" })).toBeDisabled();

    await user.type(within(dialog).getByLabelText(/Bitiş ayı/), "2026-12");
    expect(within(dialog).getByRole("button", { name: "Kaydet" })).toBeEnabled();
  });

  it("shows the server message (closed period, overlap, MaxMonths) and stays open", async () => {
    vi.mocked(createIncentiveAssignments).mockRejectedValue(serverError("Çalışanın bu kategoride bu aralıkta zaten bir teşviki var."));
    const user = userEvent.setup();
    const { onClose } = renderModal();

    const dialog = await pickDefinition(user, "SGK_5510_MD81 - 5510 md.81");
    await user.click(within(dialog).getByRole("button", { name: "pick-employee" }));
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));

    expect(await screen.findByText("Çalışanın bu kategoride bu aralıkta zaten bir teşviki var.")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("cannot be saved without an employee", async () => {
    const user = userEvent.setup();
    renderModal();

    const dialog = await pickDefinition(user, "SGK_5510_MD81 - 5510 md.81");

    expect(within(dialog).getByRole("button", { name: "Kaydet" })).toBeDisabled();
  });
});
