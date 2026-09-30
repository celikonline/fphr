import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import "@testing-library/jest-dom/vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { MemoryRouter } from "react-router";
import trDataTransfer from "../../../public/locales/tr/dataTransfer.json";
import trTerminals from "../../../public/locales/tr/terminals.json";
import enTerminals from "../../../public/locales/en/terminals.json";
import deTerminals from "../../../public/locales/de/terminals.json";
import trCommon from "../../../public/locales/tr/common.json";
import TerminalsPage from "./terminals";
import { useAuthStore } from "@/store/auth.store";
import * as svc from "@/services/attendance-terminals.service";
import type { Terminal } from "@/services/attendance-terminals.service";

vi.mock("@/services/attendance-terminals.service", async (orig) => ({
  ...(await orig<typeof import("@/services/attendance-terminals.service")>()),
  listTerminals: vi.fn(),
  createTerminal: vi.fn(),
  updateTerminal: vi.fn(),
  rotateTerminalApiKey: vi.fn(),
  setTerminalActive: vi.fn(),
  listTerminalGroups: vi.fn(),
  createTerminalGroup: vi.fn(),
  renameTerminalGroup: vi.fn(),
  deleteTerminalGroup: vi.fn(),
  listEmployeeCredentials: vi.fn(),
  addEmployeeCredential: vi.fn(),
  setCredentialActive: vi.fn(),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));
vi.mock("@/components/employees/employee-picker", () => ({
  default: ({ onChange }: { onChange: (v: { id: string; displayName: string }) => void }) => (
    <button type="button" onClick={() => onChange({ id: "e1", displayName: "Ayşe Yılmaz" })}>
      pick-employee
    </button>
  ),
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
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
});

const SECRET_KEY = "sk_live_SUPERSECRETKEY123";

function terminal(over: Partial<Terminal> = {}): Terminal {
  return {
    id: "t1",
    code: "KAPI-1",
    name: "Ana kapı",
    directionMode: "both",
    isActive: true,
    isOnline: true,
    lastSeenAtUtc: "2026-09-25T08:00:00Z",
    ...over,
  };
}

function setPermissions(...granted: string[]) {
  useAuthStore.setState({
    hasPermission: (p: string) => granted.includes(p),
    isAccessSettingEnabled: () => true,
  });
}

beforeEach(() => {
  setPermissions("attendance.terminal.view", "attendance.terminal.manage");
  vi.mocked(svc.listTerminals).mockResolvedValue([terminal()]);
  vi.mocked(svc.listTerminalGroups).mockResolvedValue([{ id: "g1", name: "Merkez" }]);
  vi.mocked(svc.listEmployeeCredentials).mockResolvedValue([]);
  vi.mocked(svc.rotateTerminalApiKey).mockResolvedValue({ terminalId: "t1", apiKey: SECRET_KEY, apiKeyPrefix: "sk_live_" });
  vi.mocked(svc.createTerminal).mockResolvedValue({ terminalId: "t2", apiKey: SECRET_KEY, apiKeyPrefix: "sk_live_" });
  vi.mocked(svc.createTerminalGroup).mockResolvedValue("g2");
  vi.mocked(svc.renameTerminalGroup).mockResolvedValue();
  vi.mocked(svc.deleteTerminalGroup).mockResolvedValue();
  vi.mocked(svc.addEmployeeCredential).mockResolvedValue();
});

afterEach(() => vi.clearAllMocks());

let lastQueryClient: QueryClient;

function renderPage(lng = "tr") {
  const instance = i18next.createInstance();
  void instance.init({
    lng,
    fallbackLng: "tr",
    defaultNS: "terminals",
    ns: ["terminals", "common", "dataTransfer"],
    resources: {
      tr: { terminals: trTerminals, common: trCommon, dataTransfer: trDataTransfer },
      en: { terminals: enTerminals, common: trCommon },
      de: { terminals: deTerminals, common: trCommon },
    },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  lastQueryClient = queryClient;
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider env="test">
          <MemoryRouter>
            <TerminalsPage />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

async function rotateKey(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: /Anahtarı yenile/ }));
  await user.click(await screen.findByRole("button", { name: "Yenile" }));
}

describe("TerminalsPage", () => {
  it("api_key_shown_once_with_copy: shows the raw key once with a warning and copies it", async () => {
    const user = userEvent.setup();
    const writeText = vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue();
    renderPage();
    await rotateKey(user);
    const dialog = await screen.findByRole("dialog", { name: "Terminal API anahtarı" });
    expect(within(dialog).getByDisplayValue(SECRET_KEY)).toBeInTheDocument();
    expect(within(dialog).getByText(/bir daha gösterilmeyecek/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole("button", { name: "Kopyala" }));
    expect(writeText).toHaveBeenCalledWith(SECRET_KEY);
    expect(svc.rotateTerminalApiKey).toHaveBeenCalledWith("t1");
  });

  it("api_key_cleared_when_dialog_closes: key is gone from DOM and storage after closing", async () => {
    const user = userEvent.setup();
    renderPage();
    await rotateKey(user);
    const dialog = await screen.findByRole("dialog", { name: "Terminal API anahtarı" });
    await user.click(within(dialog).getByRole("button", { name: "Kapat" }));
    await waitFor(() => expect(screen.queryByDisplayValue(SECRET_KEY)).not.toBeInTheDocument());
    expect(document.body.textContent).not.toContain(SECRET_KEY);
    expect(JSON.stringify({ ...localStorage })).not.toContain(SECRET_KEY);
    expect(window.location.href).not.toContain(SECRET_KEY);
    // The raw key must not linger in the mutation cache (gcTime is minutes) either.
    expect(JSON.stringify(lastQueryClient.getMutationCache().getAll().map((m) => m.state.data ?? null))).not.toContain(
      SECRET_KEY,
    );
  });

  it("create_key_not_kept_in_mutation_cache: created terminal key is handed to the dialog only", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /Yeni terminal/ }));
    await user.type(await screen.findByLabelText(/Kod/), "K1");
    await user.type(screen.getByLabelText(/^Ad/), "Kapı");
    await user.click(screen.getByRole("button", { name: "Kaydet" }));
    const dialog = await screen.findByRole("dialog", { name: "Terminal API anahtarı" });
    expect(within(dialog).getByDisplayValue(SECRET_KEY)).toBeInTheDocument();
    expect(JSON.stringify(lastQueryClient.getMutationCache().getAll().map((m) => m.state.data ?? null))).not.toContain(
      SECRET_KEY,
    );
  });

  it("set_active_uses_put_active_body: deactivate sends PUT active {isActive:false} (real T1 contract)", async () => {
    const user = userEvent.setup();
    vi.mocked(svc.setTerminalActive).mockResolvedValue();
    renderPage();
    await user.click(await screen.findByRole("button", { name: "Pasifleştir" }));
    await waitFor(() => expect(svc.setTerminalActive).toHaveBeenCalledWith("t1", false));
  });

  it("suspicious_badge_has_text: shows a textual warning with the date", async () => {
    vi.mocked(svc.listTerminals).mockResolvedValue([terminal({ lastSuspiciousAtUtc: "2026-09-25T09:00:00Z" })]);
    renderPage();
    expect(await screen.findByText(/Şüpheli PIN denemesi:/)).toBeInTheDocument();
  });

  it("import_button_opens_dialog_with_terminal_credentials_template + sensitive_notice_shown", async () => {
    const user = userEvent.setup();
    setPermissions("attendance.terminal.view", "attendance.terminal.manage", "datatransfer.import.run");
    renderPage();
    await user.click(await screen.findByRole("tab", { name: "Çalışan kimlikleri" }));
    await user.click(await screen.findByRole("button", { name: /Excel ile içe aktar/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/kart\/PIN içerir; yüklendikten sonra işlenir ve sunucudan silinir \(en geç 24 saat\)/)).toBeInTheDocument();
    expect(document.querySelector("[data-template-code]")).toHaveAttribute("data-template-code", "terminal-credentials");
  });

  it("offline_badge_has_text: presence is conveyed by text, not colour alone", async () => {
    vi.mocked(svc.listTerminals).mockResolvedValue([
      terminal(),
      terminal({ id: "t2", code: "KAPI-2", name: "Depo", isOnline: false, lastSeenAtUtc: null }),
    ]);
    renderPage();
    expect(await screen.findByText("Çevrimiçi")).toBeInTheDocument();
    expect(screen.getByText("Çevrimdışı")).toBeInTheDocument();
    expect(screen.getByText("Hiç görülmedi")).toBeInTheDocument();
  });

  it("direction_mode_labels: shows a localized label per direction mode", async () => {
    vi.mocked(svc.listTerminals).mockResolvedValue([
      terminal({ id: "a", code: "A", directionMode: "both" }),
      terminal({ id: "b", code: "B", directionMode: "inOnly" }),
      terminal({ id: "c", code: "C", directionMode: "outOnly" }),
    ]);
    renderPage();
    expect(await screen.findByText("Giriş ve çıkış")).toBeInTheDocument();
    expect(screen.getByText("Yalnız giriş")).toBeInTheDocument();
    expect(screen.getByText("Yalnız çıkış")).toBeInTheDocument();
  });

  it("direction_mode_labels: has the same key set in tr/en/de", () => {
    const keys = (o: object, p = ""): string[] =>
      Object.entries(o).flatMap(([k, v]) => (typeof v === "object" ? keys(v, `${p}${k}.`) : [`${p}${k}`]));
    expect(keys(enTerminals).sort()).toEqual(keys(trTerminals).sort());
    expect(keys(deTerminals).sort()).toEqual(keys(trTerminals).sort());
  });

  it("manage_hidden_without_permission: view-only user sees no management buttons", async () => {
    setPermissions("attendance.terminal.view");
    renderPage();
    expect(await screen.findByText("Ana kapı")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Yeni terminal/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Anahtarı yenile/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Pasifleştir" })).not.toBeInTheDocument();
  });

  it("manage_hidden_without_permission: user without any terminal permission gets NoAccess", async () => {
    setPermissions();
    renderPage();
    expect(await screen.findByText("Bu sayfaya erişiminiz yok")).toBeInTheDocument();
    expect(svc.listTerminals).not.toHaveBeenCalled();
  });

  it("group_crud: creates, renames and deletes a group", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("tab", { name: "Gruplar" }));
    expect(await screen.findByText("Merkez")).toBeInTheDocument();

    await user.type(screen.getByLabelText("Grup adı"), "Depo");
    await user.click(screen.getByRole("button", { name: "Yeni grup" }));
    await waitFor(() => expect(svc.createTerminalGroup).toHaveBeenCalledWith("Depo"));

    await user.click(screen.getByRole("button", { name: "Yeniden adlandır" }));
    const input = await screen.findByLabelText("Grup adı", { selector: "input[data-editing]" });
    await user.clear(input);
    await user.type(input, "Merkez 2");
    await user.click(screen.getByRole("button", { name: "Kaydet" }));
    await waitFor(() => expect(svc.renameTerminalGroup).toHaveBeenCalledWith("g1", "Merkez 2"));

    await user.click(screen.getByRole("button", { name: "Sil" }));
    await user.click(await screen.findByRole("button", { name: "Evet, sil" }));
    await waitFor(() => expect(svc.deleteTerminalGroup).toHaveBeenCalledWith("g1"));
  });

  it("credential_value_never_rendered: entered value is sent but never shown; list is masked", async () => {
    const user = userEvent.setup();
    vi.mocked(svc.listEmployeeCredentials).mockResolvedValue([
      { id: "c1", employeeId: "e1", kind: "card", isActive: true },
    ]);
    renderPage();
    await user.click(await screen.findByRole("tab", { name: "Çalışan kimlikleri" }));
    await user.click(await screen.findByRole("button", { name: "pick-employee" }));
    expect(within(await screen.findByRole("table")).getByText("Kart")).toBeInTheDocument();
    expect(screen.getByText("••••")).toBeInTheDocument();

    const value = screen.getByLabelText(/Değer/);
    expect(value).toHaveAttribute("type", "password");
    await user.type(value, "1234567890");
    await user.click(screen.getByRole("button", { name: "Kimlik ekle" }));
    await waitFor(() =>
      expect(svc.addEmployeeCredential).toHaveBeenCalledWith({ employeeId: "e1", kind: "card", value: "1234567890" }),
    );
    await waitFor(() => expect(screen.getByLabelText(/Değer/)).toHaveValue(""));
    expect(document.body.textContent).not.toContain("1234567890");
  });
});
