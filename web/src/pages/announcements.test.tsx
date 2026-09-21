import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAnnouncements from "../../public/locales/tr/announcements.json";
import trCommon from "../../public/locales/tr/common.json";
import AnnouncementsPage from "./announcements";
import { markAnnouncementRead, type Announcement, type AnnouncementFeedItem } from "@/services/announcements.service";

const permissions = vi.hoisted(() => ({ granted: new Set<string>() }));

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return {
    ...actual,
    usePermission: (key: string) => permissions.granted.has(key),
    useAnyPermission: (keys: string[]) => keys.some((key) => permissions.granted.has(key)),
  };
});

const FEED: AnnouncementFeedItem[] = [
  {
    id: "a1",
    title: "Yeni yan haklar",
    content: "Ekim ayından itibaren yemek kartı tutarı artıyor.",
    type: "HR",
    priority: "High",
    publishedAt: "2026-09-20T08:00:00Z",
    isRead: false,
  },
  {
    id: "a2",
    title: "Ofis taşınması",
    content: "Detaylar ekte.",
    type: "General",
    priority: "Normal",
    publishedAt: "2026-09-10T08:00:00Z",
    isRead: true,
  },
];

const MANAGED: Announcement[] = [
  {
    id: "d1",
    title: "Taslak duyuru",
    content: "İçerik",
    type: "Policy",
    priority: "Low",
    status: "Draft",
    publishedBy: "00000000-0000-0000-0000-000000000000",
    createdAt: "2026-09-21T08:00:00Z",
  },
];

vi.mock("@/services/announcements.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/announcements.service")>(
    "@/services/announcements.service"
  );
  return {
    ...actual,
    getMyAnnouncementFeed: vi.fn(async () => FEED),
    markAnnouncementRead: vi.fn(async () => {}),
    listAnnouncements: vi.fn(async () => MANAGED),
    getAnnouncementReadCount: vi.fn(async () => 0),
  };
});

// jsdom has no matchMedia/ResizeObserver; Mantine's Card/Table/Modal need these to render.
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
  vi.clearAllMocks();
  permissions.granted = new Set(["announcements.my.feed"]);
});

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "announcements",
    ns: ["announcements", "common"],
    resources: { tr: { announcements: trAnnouncements, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter>
            <AnnouncementsPage />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("AnnouncementsPage", () => {
  it("shows the live feed with translated type/priority and flags only unread items as new", async () => {
    renderPage();

    expect(await screen.findByText("Yeni yan haklar")).toBeInTheDocument();
    expect(screen.getByText("Ofis taşınması")).toBeInTheDocument();
    expect(screen.getAllByText("Yeni")).toHaveLength(1);
    expect(screen.getByText("Yüksek")).toBeInTheDocument();
    expect(screen.getByText(/^İK/)).toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Yönetim" })).not.toBeInTheDocument();
  });

  it("opens an unread announcement and marks it as read", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByText("Yeni yan haklar"));

    expect(screen.getByText("Ekim ayından itibaren yemek kartı tutarı artıyor.")).toBeInTheDocument();
    await waitFor(() => expect(markAnnouncementRead).toHaveBeenCalledWith("a1"));
    await waitFor(() => expect(screen.queryByText("Yeni")).not.toBeInTheDocument());
  });

  it("does not mark an already-read announcement again", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByText("Ofis taşınması"));

    expect(screen.getByText("Detaylar ekte.")).toBeInTheDocument();
    expect(markAnnouncementRead).not.toHaveBeenCalled();
  });

  it("gives HR a management tab listing drafts with a publish action", async () => {
    permissions.granted = new Set(["announcements.my.feed", "announcements.announcements.manage"]);
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole("tab", { name: "Yönetim" }));

    expect(await screen.findByText("Taslak duyuru")).toBeInTheDocument();
    expect(screen.getByText("Taslak")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Yayınla" })).toBeInTheDocument();
  });
});
