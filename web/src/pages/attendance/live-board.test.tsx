import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAttendance from "../../../public/locales/tr/attendance.json";
import trCommon from "../../../public/locales/tr/common.json";
import LiveBoardPage from "./live-board";
import { useAuthStore } from "@/store/auth.store";
import { getLiveAttendanceBoard } from "@/services/attendance.service";
import { getLiveTerminals } from "@/services/attendance-board.service";

vi.mock("@/services/attendance.service", () => ({ getLiveAttendanceBoard: vi.fn() }));
vi.mock("@/services/attendance-board.service", () => ({ getLiveTerminals: vi.fn() }));

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
  void i18next.init({
    lng: "tr",
    fallbackLng: "tr",
    ns: ["attendance", "common"],
    resources: { tr: { attendance: trAttendance, common: trCommon } },
    interpolation: { escapeValue: false },
  });
});

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <I18nextProvider i18n={i18next}>
        <MantineProvider>
          <LiveBoardPage />
        </MantineProvider>
      </I18nextProvider>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  useAuthStore.setState({
    hasPermission: (p: string) => p === "attendance.board.view" || p === "attendance.terminal.view",
    isAccessSettingEnabled: () => true,
  });
  vi.mocked(getLiveTerminals).mockResolvedValue([]);
});

afterEach(() => vi.clearAllMocks());

describe("LiveBoardPage", () => {
  it("terminals_offline_badge_has_text", async () => {
    vi.mocked(getLiveAttendanceBoard).mockResolvedValue([]);
    vi.mocked(getLiveTerminals).mockResolvedValue([
      { terminalId: "t1", code: "T1", name: "Ana Kapı", isActive: true, online: false, todayPunchCount: 3 },
      { terminalId: "t2", code: "T2", name: "Yan Kapı", isActive: true, online: true, todayPunchCount: 0 },
    ]);
    renderPage();
    expect(await screen.findByText("Çevrimdışı")).toBeInTheDocument();
    expect(screen.getByText("Çevrimiçi")).toBeInTheDocument();
  });

  it("row_shows_last_source", async () => {
    vi.mocked(getLiveAttendanceBoard).mockResolvedValue([
      {
        employeeId: "e1",
        employeeName: "Ayşe Yılmaz",
        checkIn: "09:00:00",
        isInside: true,
        lastPunchSource: "terminal",
        lastPunchTerminalName: "Ana Kapı",
      } as never,
    ]);
    renderPage();
    expect(await screen.findByText("Terminal · Ana Kapı")).toBeInTheDocument();
  });

  it("legacy_rows_render_without_new_fields", async () => {
    vi.mocked(getLiveAttendanceBoard).mockResolvedValue([
      { employeeId: "e1", employeeName: "Kemal Demir", checkIn: "09:00:00", isInside: true, source: "device" },
    ]);
    renderPage();
    expect(await screen.findByText("Kemal Demir")).toBeInTheDocument();
    expect(screen.getByText("İçeride")).toBeInTheDocument();
  });
});
