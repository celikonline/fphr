import { beforeAll, beforeEach, afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import trSupport from "../../public/locales/tr/support.json";
import SupportPage from "./support";
import {
  addSupportTicketComment,
  getSupportTicket,
  listMySupportTickets,
  type SupportTicket,
} from "@/services/support.service";

const auth = vi.hoisted(() => ({ user: { id: "user-a", tenant: { id: "tenant-a" } } }));
vi.mock("@/store/auth.store", () => ({
  useAuthStore: (selector: (state: typeof auth) => unknown) => selector(auth),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));
vi.mock("@/services/support.service", async (original) => ({
  ...(await original<typeof import("@/services/support.service")>()),
  searchHelpArticles: vi.fn().mockResolvedValue([]),
  listMySupportTickets: vi.fn(),
  addSupportTicketComment: vi.fn(),
  getSupportTicket: vi.fn(),
}));

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener() {},
      removeListener() {},
      addEventListener() {},
      removeEventListener() {},
      dispatchEvent: () => false,
    }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
  Object.defineProperty(document, "fonts", {
    writable: true,
    value: { addEventListener() {}, removeEventListener() {} },
  });
});
beforeEach(() => {
  vi.clearAllMocks();
  auth.user = { id: "user-a", tenant: { id: "tenant-a" } };
});
afterEach(cleanup);

const baseTicket: SupportTicket = {
  id: "local-guid",
  requestedByUserId: "user-a",
  subject: "Bordro desteği",
  body: "Bir hata var",
  category: "payroll",
  channel: "web",
  status: "open",
  sourceRoute: null,
  resolution: null,
  answeredAt: null,
  closedAt: null,
  createdAt: "2026-10-14T09:00:00Z",
  portal: {
    ticketId: "central-id",
    number: "FP-100",
    status: "waiting_requester",
    syncState: "connected",
    comments: [
      {
        id: "2",
        body: "Onaylanan çözüm adımları",
        source: "external_agent",
        createdAt: "2026-10-14T11:00:00Z",
      },
      {
        id: "1",
        body: "Hangi bordro dönemi?",
        source: "customer_agent",
        createdAt: "2026-10-14T10:00:00Z",
      },
    ],
  },
};

function renderSupport(
  client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
) {
  const i18n = i18next.createInstance();
  void i18n.init({
    lng: "tr",
    resources: { tr: { support: trSupport } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const view = render(
    <I18nextProvider i18n={i18n}>
      <MantineProvider>
        <QueryClientProvider client={client}>
          <MemoryRouter>
            <SupportPage />
          </MemoryRouter>
        </QueryClientProvider>
      </MantineProvider>
    </I18nextProvider>
  );
  return { ...view, client };
}

describe("central support tickets", () => {
  it("loads older public messages on demand through the scoped local ticket detail", async () => {
    vi.mocked(listMySupportTickets).mockResolvedValue([
      { ...baseTicket, portal: { ...baseTicket.portal!, commentsTruncated: true } },
    ]);
    vi.mocked(getSupportTicket).mockResolvedValue({
      ...baseTicket,
      portal: {
        ...baseTicket.portal!,
        commentsTruncated: false,
        comments: [
          {
            id: "old",
            body: "İlk müşterinin mesajı",
            source: "human",
            createdAt: "2026-10-14T09:30:00Z",
          },
          ...baseTicket.portal!.comments,
        ],
      },
    });
    const { client } = renderSupport();
    const user = userEvent.setup();
    const button = await screen.findByRole("button", { name: "Tüm konuşmayı göster" });
    expect(getSupportTicket).not.toHaveBeenCalled();
    expect(screen.queryByText("İlk müşterinin mesajı")).not.toBeInTheDocument();
    await user.click(button);
    expect(await screen.findByText("İlk müşterinin mesajı")).toBeInTheDocument();
    expect(getSupportTicket).toHaveBeenCalledWith("local-guid");
    expect(
      client.getQueryData(["support", "ticket", "tenant-a", "user-a", "local-guid"])
    ).toBeDefined();
    expect(screen.queryByRole("button", { name: "Tüm konuşmayı göster" })).not.toBeInTheDocument();
  });

  it("shows exact central state, chronologically ordered public AI and PO approved messages", async () => {
    vi.mocked(listMySupportTickets).mockResolvedValue([baseTicket]);
    renderSupport();
    expect(await screen.findByText("Yanıtınız bekleniyor")).toBeInTheDocument();
    expect(screen.queryByText("Açık")).not.toBeInTheDocument();
    expect(screen.getByText("AI müşteri agentı")).toBeInTheDocument();
    expect(screen.getByText("PO onaylı çözüm")).toBeInTheDocument();
    expect(screen.getAllByRole("listitem").map((item) => item.textContent)).toEqual([
      expect.stringContaining("Hangi bordro dönemi?"),
      expect.stringContaining("Onaylanan çözüm adımları"),
    ]);
  });

  it("preserves failed reply and UUID on retry, changing the UUID only for a changed body", async () => {
    vi.mocked(listMySupportTickets).mockResolvedValue([baseTicket]);
    vi.mocked(addSupportTicketComment).mockRejectedValue(new Error("offline"));
    renderSupport();
    const user = userEvent.setup();
    const input = await screen.findByLabelText("Takip yanıtınız");
    await user.type(input, "Ekim dönemi");
    await user.click(screen.getByRole("button", { name: "Yanıt gönder" }));
    await screen.findByRole("alert");
    const key = vi.mocked(addSupportTicketComment).mock.calls[0][2];
    expect(key).toMatch(/^[0-9a-f-]{36}$/);
    expect(input).toHaveValue("Ekim dönemi");
    await user.click(screen.getByRole("button", { name: "Yanıt gönder" }));
    await waitFor(() => expect(addSupportTicketComment).toHaveBeenCalledTimes(2));
    expect(vi.mocked(addSupportTicketComment).mock.calls[1]).toEqual([
      "local-guid",
      "Ekim dönemi",
      key,
    ]);
    await screen.findByRole("alert");
    await user.type(input, " güncel");
    vi.mocked(addSupportTicketComment).mockResolvedValue(undefined);
    await user.click(screen.getByRole("button", { name: "Yanıt gönder" }));
    await waitFor(() => expect(input).toHaveValue(""));
    expect(vi.mocked(addSupportTicketComment).mock.calls[2][2]).not.toEqual(key);
  });

  it("keeps legacy status visible while sync is pending or unavailable and disables followup", async () => {
    vi.mocked(listMySupportTickets).mockResolvedValue([
      { ...baseTicket, id: "pending", portal: { syncState: "pending", comments: [] } },
      {
        ...baseTicket,
        id: "unavailable",
        status: "answered",
        portal: { syncState: "unavailable", comments: [] },
      },
      { ...baseTicket, id: "legacy", status: "closed", portal: undefined },
    ]);
    renderSupport();
    expect(await screen.findByText("Destek portalına aktarım bekleniyor.")).toBeInTheDocument();
    expect(screen.getByText(/Destek portalına şu anda ulaşılamıyor/)).toBeInTheDocument();
    expect(screen.getByText("Açık")).toBeInTheDocument();
    expect(screen.getByText("Yanıtlandı")).toBeInTheDocument();
    expect(screen.getByText("Kapandı")).toBeInTheDocument();
    expect(screen.queryByLabelText("Takip yanıtınız")).not.toBeInTheDocument();
  });

  it("keeps terminal central statuses and messages visible without offering followup", async () => {
    vi.mocked(listMySupportTickets).mockResolvedValue(
      (["resolved", "closed", "cancelled"] as const).map((status) => ({
        ...baseTicket,
        id: status,
        portal: { ...baseTicket.portal!, status },
      }))
    );
    renderSupport();
    expect(await screen.findByText("Çözüldü")).toBeInTheDocument();
    expect(screen.getByText("Kapandı")).toBeInTheDocument();
    expect(screen.getByText("İptal edildi")).toBeInTheDocument();
    expect(screen.getAllByText("Onaylanan çözüm adımları")).toHaveLength(3);
    expect(screen.queryByLabelText("Takip yanıtınız")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Yanıt gönder" })).not.toBeInTheDocument();
    expect(addSupportTicketComment).not.toHaveBeenCalled();
  });

  it("reports request failure instead of claiming no tickets exist", async () => {
    vi.mocked(listMySupportTickets).mockRejectedValue(new Error("offline"));
    renderSupport();
    expect(await screen.findByRole("alert")).toHaveTextContent("Talepler yüklenemedi");
    expect(screen.queryByText("Henüz bir destek talebiniz yok.")).not.toBeInTheDocument();
  });

  it("does not reuse another user's own-ticket cache", async () => {
    vi.mocked(listMySupportTickets).mockResolvedValue([]);
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    client.setQueryData(["support", "tickets", "mine", "tenant-b", "user-b"], [baseTicket]);
    renderSupport(client);
    expect(await screen.findByText("Henüz bir destek talebiniz yok.")).toBeInTheDocument();
    expect(screen.queryByText("Bordro desteği")).not.toBeInTheDocument();
  });
});
