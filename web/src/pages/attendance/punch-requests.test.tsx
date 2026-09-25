import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trDaily from "../../../public/locales/tr/attendance-daily.json";
import trCommon from "../../../public/locales/tr/common.json";
import PunchRequestsPage from "./punch-requests";
import { useAuthStore } from "@/store/auth.store";
import {
  cancelPunchRequest,
  createPunchRequest,
  listMyPunchRequests,
  listPunchRequests,
  type PunchRequest,
} from "@/services/attendance-punch-requests.service";

vi.mock("@/services/attendance-punch-requests.service", () => ({
  createPunchRequest: vi.fn(),
  listMyPunchRequests: vi.fn(),
  listPunchRequests: vi.fn(),
  cancelPunchRequest: vi.fn(),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

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

function row(id: string, status: PunchRequest["status"]): PunchRequest {
  return {
    id,
    employeeId: "e1",
    occurredAtUtc: "2026-09-20T05:30:00Z",
    direction: "in",
    reason: `sebep-${id}`,
    status,
  };
}

beforeEach(() => {
  useAuthStore.setState({
    hasPermission: (p: string) => p === "attendance.my.punchrequest",
    isAccessSettingEnabled: () => true,
  });
  vi.mocked(listMyPunchRequests).mockResolvedValue([row("p1", "pending"), row("a1", "approved")]);
  vi.mocked(listPunchRequests).mockResolvedValue([]);
  vi.mocked(createPunchRequest).mockResolvedValue("new");
  vi.mocked(cancelPunchRequest).mockResolvedValue();
});

afterEach(() => vi.clearAllMocks());

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "attendance-daily",
    ns: ["attendance-daily", "common"],
    resources: { tr: { "attendance-daily": trDaily, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider env="test">
          <PunchRequestsPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

function pad(n: number) {
  return String(n).padStart(2, "0");
}

async function fillForm(date: string, time: string) {
  const user = userEvent.setup();
  await user.click(await screen.findByRole("button", { name: "Yeni talep" }));
  const dateInput = await screen.findByLabelText(/Tarih/);
  const timeInput = screen.getByLabelText(/Saat/);
  await user.clear(dateInput);
  await user.type(dateInput, date);
  await user.clear(timeInput);
  await user.type(timeInput, time);
  await user.type(screen.getByLabelText(/Gerekçe/), "Kart okutmayı unuttum");
  return user;
}

describe("PunchRequestsPage", () => {
  it("future_time_blocked: rejects a future time and does not call the service", async () => {
    renderPage();
    const tomorrow = new Date(Date.now() + 86_400_000);
    const date = `${tomorrow.getFullYear()}-${pad(tomorrow.getMonth() + 1)}-${pad(tomorrow.getDate())}`;
    const user = await fillForm(date, "09:00");
    await user.click(screen.getByRole("button", { name: "Talep gönder" }));
    expect(await screen.findByText("Gelecek bir zaman için talep oluşturulamaz.")).toBeInTheDocument();
    expect(createPunchRequest).not.toHaveBeenCalled();
  });

  it("submit_calls_service: sends UTC instant, direction and reason for a past time", async () => {
    renderPage();
    const user = await fillForm("2026-09-20", "08:30");
    await user.click(screen.getByRole("button", { name: "Talep gönder" }));
    await waitFor(() => expect(createPunchRequest).toHaveBeenCalledTimes(1));
    expect(vi.mocked(createPunchRequest).mock.calls[0][0]).toEqual({
      occurredAtUtc: new Date(2026, 8, 20, 8, 30).toISOString(),
      direction: "in",
      reason: "Kart okutmayı unuttum",
    });
  });

  it("status_chip_updates: shows a chip per status and refetches after submit", async () => {
    renderPage();
    expect(await screen.findByText("Bekliyor")).toBeInTheDocument();
    expect(screen.getByText("Onaylandı")).toBeInTheDocument();
    vi.mocked(listMyPunchRequests).mockResolvedValue([row("p1", "approved"), row("a1", "approved")]);
    const user = await fillForm("2026-09-20", "08:30");
    await user.click(screen.getByRole("button", { name: "Talep gönder" }));
    await waitFor(() => expect(screen.queryByText("Bekliyor")).not.toBeInTheDocument());
  });

  it("cancel_only_pending: cancel action exists only for pending rows", async () => {
    vi.mocked(listMyPunchRequests).mockResolvedValue([
      row("p1", "pending"),
      row("a1", "approved"),
      row("r1", "rejected"),
      row("c1", "cancelled"),
    ]);
    renderPage();
    await screen.findByText("Bekliyor");
    expect(screen.getAllByRole("button", { name: "İptal et" })).toHaveLength(1);
    const user = userEvent.setup();
    await user.click(screen.getByRole("button", { name: "İptal et" }));
    await user.click((await screen.findAllByRole("button", { name: "İptal et" })).slice(-1)[0]);
    await waitFor(() => expect(cancelPunchRequest).toHaveBeenCalledWith("p1"));
  });
});
