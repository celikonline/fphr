import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trPayroll from "../../../public/locales/tr/payroll.json";
import trCommon from "../../../public/locales/tr/common.json";
import { MyPayslips } from "./my-payslips";
import {
  acknowledgePayslip,
  listMyPayslips,
  objectToPayslip,
  type MyPayslip,
} from "@/services/payslip-ack.service";

vi.mock("@/services/payslip-ack.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/payslip-ack.service")>(
    "@/services/payslip-ack.service"
  );
  return {
    ...actual,
    listMyPayslips: vi.fn(),
    acknowledgePayslip: vi.fn(),
    objectToPayslip: vi.fn(),
  };
});

const published: MyPayslip = {
  payslipId: "p1", year: 2026, month: 8, grossPay: 50000, netPay: 38000,
  publishedAt: "2026-09-01T08:00:00Z",
};
const done: MyPayslip = { ...published, payslipId: "p2", month: 7, acknowledgedAt: "2026-08-05T08:00:00Z" };

beforeAll(async () => {
  await i18next.init({
    lng: "tr", fallbackLng: "tr", ns: ["payroll", "common"], defaultNS: "payroll",
    resources: { tr: { payroll: trPayroll, common: trCommon } },
  });
  vi.stubGlobal("ResizeObserver", class { observe() {} unobserve() {} disconnect() {} });
  (window as unknown as { matchMedia: unknown }).matchMedia ??= () => ({
    matches: false, addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {},
  });
});

function renderIt() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MantineProvider>
      <QueryClientProvider client={client}>
        <I18nextProvider i18n={i18next}>
          <MyPayslips />
        </I18nextProvider>
      </QueryClientProvider>
    </MantineProvider>
  );
}

describe("MyPayslips", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(listMyPayslips).mockResolvedValue([published, done]);
    vi.mocked(acknowledgePayslip).mockResolvedValue();
    vi.mocked(objectToPayslip).mockResolvedValue();
  });

  it("list_shows_ack_status", async () => {
    renderIt();
    expect(await screen.findByText("Onay bekliyor")).toBeTruthy();
    expect(screen.getByText("Onaylandı")).toBeTruthy();
  });

  it("acknowledge_disabled_after_done", async () => {
    renderIt();
    await screen.findByText("Onaylandı");
    const buttons = screen.getAllByRole("button", { name: "Onayla" });
    expect((buttons[0] as HTMLButtonElement).disabled).toBe(false);
    expect((buttons[1] as HTMLButtonElement).disabled).toBe(true);
  });

  it("disables acknowledge while an objection is pending and shows a hint", async () => {
    vi.mocked(listMyPayslips).mockResolvedValue([{ ...published, objectionStatus: "pending" }]);
    renderIt();
    const btn = await screen.findByRole("button", { name: "Onayla" });
    expect((btn as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getAllByText("İtirazınız inceleniyor").length).toBeGreaterThan(0);
  });

  it("re-enables acknowledge after a resolved objection", async () => {
    vi.mocked(listMyPayslips).mockResolvedValue([{ ...published, objectionStatus: "rejected" }]);
    renderIt();
    const btn = await screen.findByRole("button", { name: "Onayla" });
    expect((btn as HTMLButtonElement).disabled).toBe(false);
  });

  it("disables object after a resolved objection or an acknowledgement", async () => {
    vi.mocked(listMyPayslips).mockResolvedValue([
      { ...published, payslipId: "a", objectionStatus: "rejected" },
      { ...published, payslipId: "b", objectionStatus: "accepted" },
      done,
      { ...published, payslipId: "c", month: 6 },
    ]);
    renderIt();
    await screen.findAllByRole("button", { name: "İtiraz et" });
    const buttons = screen.getAllByRole("button", { name: "İtiraz et" }) as HTMLButtonElement[];
    expect(buttons.map((b) => b.disabled)).toEqual([true, true, true, false]);
  });

  it("shows gross pay when present", async () => {
    renderIt();
    expect(await screen.findAllByText(/50\.000,00/)).toBeTruthy();
  });

  it("acknowledges a published payslip", async () => {
    renderIt();
    const user = userEvent.setup();
    await user.click((await screen.findAllByRole("button", { name: "Onayla" }))[0]);
    await waitFor(() => expect(acknowledgePayslip).toHaveBeenCalledWith("p1"));
  });

  it("object_requires_reason", async () => {
    renderIt();
    const user = userEvent.setup();
    await user.click((await screen.findAllByRole("button", { name: "İtiraz et" }))[0]);
    await user.click(await screen.findByRole("button", { name: "İtirazı gönder" }));
    expect(await screen.findByText("Gerekçe zorunludur.")).toBeTruthy();
    expect(objectToPayslip).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText(/İtiraz gerekçesi/), "Fazla mesai eksik");
    await user.click(screen.getByRole("button", { name: "İtirazı gönder" }));
    await waitFor(() => expect(objectToPayslip).toHaveBeenCalledWith("p1", "Fazla mesai eksik"));
  });
});
