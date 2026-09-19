import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trSettings from "../../../public/locales/tr/settings.json";
import trCommon from "../../../public/locales/tr/common.json";
import ModuleSettingsPage from "./modules";
import { getModuleSettings, updateModuleSettings } from "@/services/module-settings.service";
import { useAuthStore } from "@/store/auth.store";
import type { ModuleSettings } from "@/types";

vi.mock("@/services/module-settings.service", () => ({
  getModuleSettings: vi.fn(),
  updateModuleSettings: vi.fn(),
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

const SETTINGS: ModuleSettings = {
  modules: [
    { key: "employee", isCore: true, enabled: true },
    { key: "leave", isCore: false, enabled: true },
    { key: "payroll", isCore: false, enabled: true },
  ],
  disabledModules: [],
};

function setPermissions(permissions: string[]) {
  useAuthStore.setState({
    token: null,
    user: { id: "u1", email: "a@b.c", name: "A", roles: [], roleLabels: [], permissions },
    features: { access: {} as never, accessSettingsBypass: true, mfaSetupRequired: false, disabledModules: [] },
    refreshMe: vi.fn().mockResolvedValue(undefined),
  });
}

function renderPage() {
  const i18n = i18next.createInstance();
  void i18n.init({
    lng: "tr",
    fallbackLng: "tr",
    ns: ["settings", "common"],
    resources: { tr: { settings: trSettings, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <ModuleSettingsPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("ModuleSettingsPage (senseik-057)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getModuleSettings).mockResolvedValue(SETTINGS);
    vi.mocked(updateModuleSettings).mockResolvedValue(undefined);
  });

  it("lists toggleable modules with switches and core modules as locked", async () => {
    setPermissions(["identity.tenant.view", "identity.modules.manage"]);
    renderPage();

    expect(await screen.findByRole("switch", { name: "İzin" })).toBeChecked();
    expect(screen.getByRole("switch", { name: "Bordro" })).toBeChecked();
    expect(screen.queryByRole("switch", { name: "Çalışanlar" })).not.toBeInTheDocument();
    expect(screen.getByText("Çalışanlar")).toBeInTheDocument();
    expect(screen.getByText("Temel modül")).toBeInTheDocument();
  });

  it("asks for confirmation before disabling, then saves the disabled list", async () => {
    setPermissions(["identity.tenant.view", "identity.modules.manage"]);
    renderPage();

    fireEvent.click(await screen.findByRole("switch", { name: "İzin" }));
    expect(await screen.findByText(/Veriler silinmez, sadece gizlenir/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Kapat" }));

    await waitFor(() => expect(screen.getByRole("switch", { name: "İzin" })).not.toBeChecked());
    fireEvent.click(screen.getByRole("button", { name: trCommon.save }));

    await waitFor(() => expect(updateModuleSettings).toHaveBeenCalled());
    expect(vi.mocked(updateModuleSettings).mock.calls[0][0]).toEqual(["leave"]);
  });

  it("is read-only without identity.modules.manage", async () => {
    setPermissions(["identity.tenant.view"]);
    renderPage();

    expect(await screen.findByRole("switch", { name: "İzin" })).toBeDisabled();
    expect(screen.queryByRole("button", { name: trCommon.save })).not.toBeInTheDocument();
  });

  it("shows no-access without any modules permission", () => {
    setPermissions([]);
    renderPage();

    expect(screen.queryByRole("switch")).not.toBeInTheDocument();
    expect(getModuleSettings).not.toHaveBeenCalled();
  });
});
