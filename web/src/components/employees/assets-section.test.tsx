import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError, type AxiosResponse } from "axios";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAssets from "../../../public/locales/tr/assets.json";
import trCommon from "../../../public/locales/tr/common.json";
import { AssetsSection } from "./assets-section";
import type { EmployeeDetail } from "@/services/employees.service";
import { toDateOnly } from "@/lib/dates";
import {
  getEmployeeAssetHistory,
  getEmployeeAssets,
  getMyAssetHistory,
  getMyAssets,
  returnAsset,
  type Asset,
  type EmployeeAssetHistoryItem,
} from "@/services/assets.service";

const permissions = vi.hoisted(() => ({ granted: new Set<string>() }));

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>(
    "@/components/permission-guard"
  );
  return { ...actual, usePermission: (key: string) => permissions.granted.has(key) };
});

vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

vi.mock("@/services/assets.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/assets.service")>(
    "@/services/assets.service"
  );
  return {
    ...actual,
    getEmployeeAssets: vi.fn(),
    getMyAssets: vi.fn(),
    getEmployeeAssetHistory: vi.fn(),
    getMyAssetHistory: vi.fn(),
    listAssets: vi.fn(async () => ({
      items: [],
      page: 1,
      pageSize: 100,
      totalCount: 0,
      totalPages: 0,
    })),
    listAssetCategories: vi.fn(async () => [{ id: "c1", code: "LAPTOP", name: "Dizüstü" }]),
    returnAsset: vi.fn(async () => {}),
  };
});

const ASSIGNED: Asset = {
  id: "as1",
  categoryId: "c1",
  name: "MacBook Pro",
  serialNumber: "SN-1",
  description: null,
  status: "assigned",
  assignedEmployeeId: "e1",
  issuedAt: "2026-09-01",
  returnedAt: null,
};

const OPEN_ROW: EmployeeAssetHistoryItem = {
  id: "h1",
  employeeId: "e1",
  assetId: "as1",
  assetName: "MacBook Pro",
  categoryId: "c1",
  issuedAt: "2026-09-01",
};
const CLOSED_ROW: EmployeeAssetHistoryItem = {
  id: "h2",
  employeeId: "e1",
  assetId: "as9",
  assetName: "Eski Telefon",
  categoryId: "c1",
  serialNumber: "OLD-9",
  issuedAt: "2025-01-01",
  returnedAt: "2025-06-30",
  endReason: "returned",
  returnCondition: "damaged",
  note: "Cam kırık",
};
const LOST_ROW: EmployeeAssetHistoryItem = {
  id: "h3",
  employeeId: "e1",
  assetId: "as8",
  assetName: "Kayıp Tablet",
  categoryId: "c1",
  issuedAt: "2025-07-01",
  returnedAt: "2025-08-01",
  endReason: "lost",
};

const FEATURE_DISABLED = "Bu özellik şirketinizin erişim ayarlarında kapatılmış.";

/** 403 ProblemDetails the API returns while the tenant switched the feature off. */
function featureDisabled(): AxiosError {
  return new AxiosError("Forbidden", "ERR_BAD_REQUEST", undefined, undefined, {
    status: 403,
    data: { code: "auth.feature_disabled", detail: FEATURE_DISABLED, status: 403 },
  } as AxiosResponse);
}

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
  Element.prototype.scrollIntoView = () => {};
  // Mantine autosize Textarea (return note) listens to document.fonts, which jsdom lacks.
  Object.defineProperty(document, "fonts", {
    writable: true,
    value: { addEventListener: () => {}, removeEventListener: () => {} },
  });
});

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getEmployeeAssets).mockResolvedValue([ASSIGNED]);
  vi.mocked(getMyAssets).mockResolvedValue([ASSIGNED]);
  vi.mocked(getEmployeeAssetHistory).mockResolvedValue([OPEN_ROW, LOST_ROW, CLOSED_ROW]);
  vi.mocked(getMyAssetHistory).mockResolvedValue([OPEN_ROW, CLOSED_ROW]);
});

function renderSection() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "assets",
    ns: ["assets", "common"],
    resources: { tr: { assets: trAssets, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter>
            <AssetsSection employee={{ id: "e1" } as EmployeeDetail} />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("AssetsSection history (employee card)", () => {
  it("lists the employee's closed assignments below the current assets for HR", async () => {
    permissions.granted = new Set(["assets.assets.view"]);
    renderSection();

    expect(await screen.findByText("Eski Telefon")).toBeInTheDocument();
    expect(getEmployeeAssetHistory).toHaveBeenCalledWith("e1");
    expect(getMyAssetHistory).not.toHaveBeenCalled();
    expect(screen.getByRole("heading", { name: "Geçmiş zimmetler" })).toBeInTheDocument();
    expect(screen.getByText("İade durumu: Hasarlı")).toBeInTheDocument();
    expect(screen.getByText("Not: Cam kırık")).toBeInTheDocument();
    expect(screen.getByText("Kayıp Tablet")).toBeInTheDocument();
    expect(screen.getByText("Kayıp")).toBeInTheDocument();
    // The open assignment belongs to the current list, never to the past one.
    expect(screen.getAllByText("MacBook Pro")).toHaveLength(1);
  });

  it("falls back to the caller's own history without assets.assets.view", async () => {
    permissions.granted = new Set(["assets.my.assets"]);
    renderSection();

    expect(await screen.findByText("Eski Telefon")).toBeInTheDocument();
    expect(getMyAssetHistory).toHaveBeenCalled();
    expect(getEmployeeAssetHistory).not.toHaveBeenCalled();
  });

  it("shows an empty hint when nothing was closed yet", async () => {
    permissions.granted = new Set(["assets.assets.view"]);
    vi.mocked(getEmployeeAssetHistory).mockResolvedValue([OPEN_ROW]);
    renderSection();

    expect(
      await screen.findByText("İade edilmiş veya kayıp bildirilmiş zimmet yok.")
    ).toBeInTheDocument();
  });

  it("returns an asset with the chosen condition and note from the card", async () => {
    permissions.granted = new Set(["assets.assets.view", "assets.assets.manage"]);
    const user = userEvent.setup();
    renderSection();

    await user.click(await screen.findByRole("button", { name: "İade al: MacBook Pro" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("combobox", { name: "İade durumu" }));
    await user.click(await screen.findByRole("option", { name: "Orta", hidden: true }));
    await user.type(within(dialog).getByLabelText("İade notu"), "Çizik var");
    await user.click(within(dialog).getByRole("button", { name: "İade al" }));

    await waitFor(() =>
      expect(returnAsset).toHaveBeenCalledWith("as1", toDateOnly(new Date()), "fair", "Çizik var")
    );
  });

  it("shows the server message when the tenant switched the feature off", async () => {
    permissions.granted = new Set(["assets.my.assets"]);
    vi.mocked(getMyAssets).mockRejectedValue(featureDisabled());
    vi.mocked(getMyAssetHistory).mockRejectedValue(featureDisabled());
    renderSection();

    expect(await screen.findAllByText(FEATURE_DISABLED)).not.toHaveLength(0);
    expect(screen.queryByText("Zimmetler yüklenemedi.")).not.toBeInTheDocument();
  });
});
