import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trMessages from "../../../public/locales/tr/messages.json";
import trCommon from "../../../public/locales/tr/common.json";
import ComposeTab from "./compose";
import GroupsTab from "./groups";
import InboxTab from "./inbox";

const permissions = vi.hoisted(() => ({ granted: new Set<string>() }));
vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>(
    "@/components/permission-guard"
  );
  return { ...actual, usePermission: (key: string) => permissions.granted.has(key) };
});

const SELECTION = { employeeIds: ["e1", "e2"], filter: null, excludedEmployeeIds: null };
vi.mock("@/components/employees/employee-multi-picker", () => ({
  default: ({ onConfirm }: { onConfirm: (s: unknown) => void }) => (
    <button onClick={() => onConfirm(SELECTION)}>picker-confirm</button>
  ),
}));

vi.mock("@/services/hr-messages.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/hr-messages.service")>(
    "@/services/hr-messages.service"
  );
  return {
    ...actual,
    sendHrMessage: vi.fn(),
    listInbox: vi.fn(),
    listOutbox: vi.fn(),
    listMessageGroups: vi.fn(),
    createMessageGroup: vi.fn(),
    updateMessageGroup: vi.fn(),
    deleteMessageGroup: vi.fn(),
  };
});
import {
  listInbox,
  deleteMessageGroup,
  listMessageGroups,
  sendHrMessage,
  updateMessageGroup,
} from "@/services/hr-messages.service";

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
  Object.defineProperty(document, "fonts", {
    writable: true,
    value: { addEventListener: () => {}, removeEventListener: () => {} },
  });
});

function renderUi(ui: React.ReactNode) {
  const i18n = i18next.createInstance();
  void i18n.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "messages",
    ns: ["common", "messages"],
    resources: { tr: { common: trCommon, messages: trMessages } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>{ui}</MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

const sendButton = () => screen.getByRole("button", { name: trMessages.compose.send });

async function fillMessage(user: ReturnType<typeof userEvent.setup>, body = "Merhaba") {
  await user.type(screen.getByRole("textbox", { name: /Başlık/ }), "Duyuru");
  await user.click(screen.getByRole("textbox", { name: /İçerik/ }));
  await user.paste(body);
}

describe("messages", () => {
  beforeEach(() => {
    permissions.granted = new Set([
      "notifications.messages.send",
      "notifications.messagegroups.manage",
    ]);
    vi.mocked(sendHrMessage).mockReset().mockResolvedValue({ id: "m1", recipientCount: 2 });
    vi.mocked(listMessageGroups).mockReset().mockResolvedValue([]);
    vi.mocked(listInbox).mockReset();
    vi.mocked(updateMessageGroup).mockReset().mockResolvedValue();
  });

  it("compose_requires_recipients", async () => {
    const user = userEvent.setup();
    renderUi(<ComposeTab />);
    await fillMessage(user);
    expect(sendButton()).toBeDisabled();
    await user.click(screen.getByRole("button", { name: trMessages.compose.pickEmployees }));
    await user.click(await screen.findByRole("button", { name: "picker-confirm" }));
    await waitFor(() => expect(sendButton()).toBeEnabled());
  });

  it("sms_channel_disabled_coming_soon", () => {
    renderUi(<ComposeTab />);
    const sms = screen.getByRole("checkbox", { name: /SMS/ });
    expect(sms).toBeDisabled();
    expect(sms).not.toBeChecked();
    expect(screen.getAllByText(new RegExp(trMessages.channels.smsSoon)).length).toBeGreaterThan(0);
  });

  it("content_counter_4096", async () => {
    const user = userEvent.setup();
    renderUi(<ComposeTab />);
    expect(screen.getByTestId("body-counter")).toHaveTextContent("0 / 4096");
    await user.click(screen.getByRole("textbox", { name: /İçerik/ }));
    await user.paste("abcde");
    expect(screen.getByTestId("body-counter")).toHaveTextContent("5 / 4096");
    expect(screen.getByRole("textbox", { name: /İçerik/ })).toHaveAttribute("maxlength", "4096");
  });

  it("send_calls_service_with_selection_and_channels", async () => {
    const user = userEvent.setup();
    renderUi(<ComposeTab />);
    await fillMessage(user, "Merhaba dünya");
    await user.click(screen.getByRole("button", { name: trMessages.compose.pickEmployees }));
    await user.click(await screen.findByRole("button", { name: "picker-confirm" }));
    await user.click(screen.getByRole("checkbox", { name: trMessages.channels.in_app }));
    await user.click(screen.getByRole("radio", { name: trMessages.importance.high }));
    await waitFor(() => expect(sendButton()).toBeEnabled());
    await user.click(sendButton());
    await waitFor(() =>
      expect(vi.mocked(sendHrMessage).mock.calls[0]?.[0]).toEqual({
        subject: "Duyuru",
        body: "Merhaba dünya",
        importance: "high",
        channels: ["email"],
        selection: SELECTION,
      })
    );
  });

  it("inbox_lists_recipients_summary", async () => {
    vi.mocked(listInbox).mockResolvedValue({
      items: [
        {
          id: "m1",
          senderUserId: "u1",
          subject: "Bayram tatili",
          body: "İyi bayramlar",
          importance: "high",
          channels: ["email", "in_app"],
          recipientCount: 31,
          sentAt: "2026-09-20T08:00:00Z",
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });
    renderUi(<InboxTab />);
    expect(await screen.findByText("Bayram tatili")).toBeInTheDocument();
    expect(screen.getByText("31 alıcı")).toBeInTheDocument();
    expect(screen.getByText(trMessages.importance.high)).toBeInTheDocument();
    expect(screen.getByText(trMessages.channels.email)).toBeInTheDocument();
  });

  it("group_edit_uses_picker", async () => {
    const user = userEvent.setup();
    vi.mocked(listMessageGroups).mockResolvedValue([
      {
        id: "g1",
        name: "Muhasebe",
        selection: { employeeIds: ["x"], filter: null, excludedEmployeeIds: null },
      },
    ]);
    renderUi(<GroupsTab />);
    await user.click(await screen.findByRole("button", { name: trMessages.groups.edit }));
    await user.click(
      await screen.findByRole("button", { name: trMessages.compose.changeEmployees })
    );
    await user.click(await screen.findByRole("button", { name: "picker-confirm" }));
    await user.click(screen.getByRole("button", { name: trMessages.groups.save }));
    await waitFor(() =>
      expect(vi.mocked(updateMessageGroup).mock.calls[0]?.slice(0, 2)).toEqual([
        "g1",
        { name: "Muhasebe", selection: SELECTION },
      ])
    );
  });

  it("inbox_row_opens_with_keyboard", async () => {
    const user = userEvent.setup();
    vi.mocked(listInbox).mockResolvedValue({
      items: [
        {
          id: "m1",
          senderUserId: "u1",
          subject: "Bayram tatili",
          body: "İyi bayramlar dilerim",
          importance: "normal",
          channels: ["email"],
          recipientCount: 3,
          sentAt: "2026-09-20T08:00:00Z",
        },
      ],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });
    renderUi(<InboxTab />);
    await screen.findByText("Bayram tatili");
    await user.tab();
    const opener = screen.getByRole("button", { name: "Mesajı aç: Bayram tatili" });
    expect(opener).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(await screen.findByText("İyi bayramlar dilerim")).toBeInTheDocument();
    await user.keyboard("{Escape}");
    await waitFor(() =>
      expect(screen.queryByText("İyi bayramlar dilerim")).not.toBeInTheDocument()
    );
    await waitFor(() => expect(opener).toHaveFocus());
  });

  it("group_delete_asks_confirmation_and_filter_group_is_dynamic", async () => {
    const user = userEvent.setup();
    vi.mocked(listMessageGroups).mockResolvedValue([
      {
        id: "g2",
        name: "Dinamik",
        selection: { employeeIds: null, filter: {}, excludedEmployeeIds: [] },
      },
    ]);
    renderUi(<GroupsTab />);
    expect(await screen.findByText(trMessages.groups.dynamic)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: trMessages.groups.delete }));
    expect(await screen.findByText(/Dinamik” grubu silinsin mi\?/)).toBeInTheDocument();
    expect(deleteMessageGroup).not.toHaveBeenCalled();
  });
});
