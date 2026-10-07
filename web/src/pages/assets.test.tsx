import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AxiosError, type AxiosResponse } from "axios";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAssets from "../../public/locales/tr/assets.json";
import trCommon from "../../public/locales/tr/common.json";
import trEmployees from "../../public/locales/tr/employees.json";
import AssetsPage from "./assets";
import { toast } from "@/hooks/use-toast";
import { addDaysToDateOnly, toDateOnly } from "@/lib/dates";
import {
  acknowledgeAsset,
  getAssetHistory,
  getAssetSummary,
  getMyAssetHistory,
  getMyAssets,
  listAssets,
  markAssetLost,
  retireAsset,
  returnAsset,
  type Asset,
  type AssetAssignment,
  type AssetSummary,
  type EmployeeAssetHistoryItem,
} from "@/services/assets.service";

const permissions = vi.hoisted(() => ({ granted: new Set<string>() }));

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>(
    "@/components/permission-guard"
  );
  return {
    ...actual,
    usePermission: (key: string) => permissions.granted.has(key),
    useAnyPermission: (keys: string[]) => keys.some((key) => permissions.granted.has(key)),
  };
});

const INVENTORY: Asset[] = [
  {
    id: "as1",
    categoryId: "c1",
    name: "MacBook Pro",
    serialNumber: "SN-1",
    description: null,
    status: "assigned",
    assignedEmployeeId: "e1",
    issuedAt: "2026-09-01",
    returnedAt: null,
  },
  {
    id: "as2",
    categoryId: "c2",
    name: "iPhone 16",
    serialNumber: null,
    description: null,
    status: "unassigned",
    assignedEmployeeId: null,
    issuedAt: null,
    returnedAt: null,
  },
];

function page<T>(items: T[]) {
  return {
    items,
    page: 1,
    pageSize: 20,
    totalCount: items.length,
    totalPages: 1,
    hasNext: false,
    hasPrevious: false,
  };
}

const SUMMARY: AssetSummary = {
  total: 3,
  unassigned: 1,
  assigned: 1,
  pendingAssignment: 0,
  retired: 0,
  lost: 2,
  unacknowledged: 1,
  warrantyExpiring: 1,
  warrantyExpired: 2,
  replacementOverdue: 4,
  categories: [],
};

vi.mock("@/services/assets.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/assets.service")>(
    "@/services/assets.service"
  );
  return {
    ...actual,
    listAssets: vi.fn(),
    getAssetSummary: vi.fn(),
    getAssetHistory: vi.fn(),
    getMyAssets: vi.fn(),
    getMyAssetHistory: vi.fn(),
    listAssetCategories: vi.fn(async () => [
      { id: "c1", code: "LAPTOP", name: "Dizüstü" },
      { id: "c2", code: "PHONE", name: "Telefon" },
    ]),
    retireAsset: vi.fn(async () => {}),
    returnAsset: vi.fn(async () => {}),
    markAssetLost: vi.fn(async () => {}),
    acknowledgeAsset: vi.fn(async () => {}),
  };
});

vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

vi.mock("@/services/employees.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/employees.service")>(
    "@/services/employees.service"
  );
  return { ...actual, getEmployee: vi.fn(async () => ({ id: "e1", displayName: "Ayşe Demir" })) };
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
  // Mantine Select scrolls the active option into view.
  Element.prototype.scrollIntoView = () => {};
  // Mantine autosize Textarea (return/lost notes) listens to document.fonts, which jsdom lacks.
  Object.defineProperty(document, "fonts", {
    writable: true,
    value: { addEventListener: () => {}, removeEventListener: () => {} },
  });
});

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(listAssets).mockImplementation(async () => page(INVENTORY));
  vi.mocked(getAssetSummary).mockResolvedValue(SUMMARY);
  vi.mocked(getAssetHistory).mockResolvedValue([]);
  vi.mocked(getMyAssets).mockResolvedValue([INVENTORY[0]!]);
  vi.mocked(getMyAssetHistory).mockResolvedValue([]);
});

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "assets",
    ns: ["assets", "common", "employees"],
    resources: { tr: { assets: trAssets, common: trCommon, employees: trEmployees } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter>
            <AssetsPage />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("AssetsPage", () => {
  it("shows only the caller's own assets to an employee", async () => {
    permissions.granted = new Set(["assets.my.assets"]);
    renderPage();

    expect(await screen.findByText("MacBook Pro")).toBeInTheDocument();
    expect(screen.queryByText("iPhone 16")).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Envanter" })).not.toBeInTheDocument();
    expect(listAssets).not.toHaveBeenCalled();
  });

  it("lists the inventory with category, status and the assignee's name for HR", async () => {
    permissions.granted = new Set(["assets.assets.view"]);
    renderPage();

    const macRow = (await screen.findByText("MacBook Pro")).closest("tr")!;
    expect(await within(macRow).findByText("Dizüstü")).toBeInTheDocument();
    expect(within(macRow).getByText("Atanmış")).toBeInTheDocument();
    expect(await within(macRow).findByRole("link", { name: "Ayşe Demir" })).toHaveAttribute(
      "href",
      "/app/employees/e1/assets"
    );
    expect(
      within(screen.getByText("iPhone 16").closest("tr")!).getByText("Boşta")
    ).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Zimmet ekle" })).not.toBeInTheDocument();
  });

  it("offers assign/retire for idle assets and return for assigned ones to managers", async () => {
    permissions.granted = new Set(["assets.assets.view", "assets.assets.manage"]);
    const user = userEvent.setup();
    renderPage();

    const macRow = (await screen.findByText("MacBook Pro")).closest("tr")!;
    const phoneRow = screen.getByText("iPhone 16").closest("tr")!;
    expect(within(macRow).getByRole("button", { name: "İade al: MacBook Pro" })).toBeInTheDocument();
    expect(
      within(macRow).queryByRole("button", { name: "Kullanım dışı bırak: MacBook Pro" })
    ).not.toBeInTheDocument();
    expect(within(phoneRow).getByRole("button", { name: "Zimmet ata: iPhone 16" })).toBeInTheDocument();

    await user.click(within(phoneRow).getByRole("button", { name: "Kullanım dışı bırak: iPhone 16" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Kullanım dışı bırak" }));

    expect(retireAsset).toHaveBeenCalledWith("as2");
  });
});

const FEATURE_DISABLED = "Bu özellik şirketinizin erişim ayarlarında kapatılmış.";

/** 403 ProblemDetails the API returns while the tenant switched the feature off. */
function featureDisabled(): AxiosError {
  return new AxiosError("Forbidden", "ERR_BAD_REQUEST", undefined, undefined, {
    status: 403,
    data: { code: "auth.feature_disabled", detail: FEATURE_DISABLED, status: 403 },
  } as AxiosResponse);
}

const today = () => toDateOnly(new Date());

const EXTRA_INVENTORY: Asset[] = [
  ...INVENTORY,
  {
    id: "as3",
    categoryId: "c1",
    name: "Dell Monitör",
    serialNumber: null,
    description: null,
    status: "pendingAssignment",
    assignedEmployeeId: null,
    issuedAt: null,
    returnedAt: null,
  },
  {
    id: "as4",
    categoryId: "c2",
    name: "Kayıp Tablet",
    serialNumber: null,
    description: null,
    status: "lost",
    assignedEmployeeId: null,
    issuedAt: null,
    returnedAt: null,
    warrantyEndsOn: "2020-01-01",
  },
  {
    id: "as5",
    categoryId: "c1",
    name: "Eski Yazıcı",
    serialNumber: null,
    description: null,
    status: "unassigned",
    assignedEmployeeId: null,
    issuedAt: null,
    returnedAt: null,
    assetTag: "DMB-77",
    brand: "HP",
    model: "LaserJet",
    purchaseDate: "2019-01-01",
    purchaseCost: 12345.5,
    warrantyEndsOn: "2020-01-01",
    condition: "damaged",
  },
  {
    id: "as6",
    categoryId: "c1",
    name: "Yeni Dizüstü",
    serialNumber: null,
    description: null,
    status: "unassigned",
    assignedEmployeeId: null,
    issuedAt: null,
    returnedAt: null,
    warrantyEndsOn: addDaysToDateOnly(toDateOnly(new Date()), 30),
  },
];

describe("AssetInventory v2", () => {
  it("shows summary counters and applies the status filter from a clickable card", async () => {
    permissions.granted = new Set(["assets.assets.view"]);
    const user = userEvent.setup();
    renderPage();

    const idleCard = await screen.findByRole("button", { name: /^Boşta\s*1$/ });
    expect(screen.getByRole("button", { name: /^Kayıp\s*2$/ })).toBeInTheDocument();
    // Read-only counters are not buttons, but their numbers are shown.
    expect(screen.getByText("KKD yenilemesi geciken").parentElement).toHaveTextContent("4");
    expect(screen.queryByRole("button", { name: /^Garantisi biten/ })).not.toBeInTheDocument();
    expect(idleCard).toHaveAttribute("aria-pressed", "false");

    await user.click(idleCard);

    await waitFor(() =>
      expect(listAssets).toHaveBeenLastCalledWith(expect.objectContaining({ status: "unassigned" }))
    );
    expect(screen.getByRole("button", { name: /^Boşta\s*1$/ })).toHaveAttribute(
      "aria-pressed",
      "true"
    );

    await user.click(screen.getByRole("button", { name: /^Toplam\s*3$/ }));
    await waitFor(() =>
      expect(listAssets).toHaveBeenLastCalledWith(expect.objectContaining({ status: undefined }))
    );
  });

  it("sends the debounced search text to the list endpoint", async () => {
    permissions.granted = new Set(["assets.assets.view"]);
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("MacBook Pro");

    await user.type(screen.getByRole("textbox", { name: "Zimmet ara" }), "  mac ");

    await waitFor(() =>
      expect(listAssets).toHaveBeenLastCalledWith(expect.objectContaining({ search: "mac" }))
    );
  });

  it("renders the new fields, status labels and warranty/receipt badges", async () => {
    permissions.granted = new Set(["assets.assets.view", "assets.assets.manage"]);
    vi.mocked(listAssets).mockImplementation(async () => page(EXTRA_INVENTORY));
    renderPage();

    const macRow = (await screen.findByText("MacBook Pro")).closest("tr")!;
    expect(within(macRow).getByText("Teslim onayı bekliyor")).toBeInTheDocument();

    const pendingRow = screen.getByText("Dell Monitör").closest("tr")!;
    expect(within(pendingRow).getByText("Atama onayında")).toBeInTheDocument();
    expect(
      within(pendingRow).queryByRole("button", { name: "Kayıp bildir: Dell Monitör" })
    ).not.toBeInTheDocument();
    expect(within(pendingRow).getByRole("button", { name: "Atama geçmişi: Dell Monitör" })).toBeInTheDocument();

    // A lost asset: badge, history stays reachable, no management actions and no warranty follow-up.
    const lostRow = screen.getByText("Kayıp Tablet").closest("tr")!;
    expect(within(lostRow).getByText("Kayıp")).toBeInTheDocument();
    expect(within(lostRow).getByRole("button", { name: "Atama geçmişi: Kayıp Tablet" })).toBeInTheDocument();
    expect(within(lostRow).queryByRole("button", { name: "Düzenle: Kayıp Tablet" })).not.toBeInTheDocument();
    expect(within(lostRow).queryByText(/Garanti bitti/)).not.toBeInTheDocument();

    const printerRow = screen.getByText("Eski Yazıcı").closest("tr")!;
    expect(within(printerRow).getByText("HP LaserJet")).toBeInTheDocument();
    expect(within(printerRow).getByText("DMB-77")).toBeInTheDocument();
    expect(within(printerRow).getByText(/Garanti bitti/)).toBeInTheDocument();
    expect(within(printerRow).getByText("Hasarlı")).toBeInTheDocument();
    expect(within(printerRow).getByText("12.345,50")).toBeInTheDocument();
    expect(within(printerRow).queryByText(/₺/)).not.toBeInTheDocument();

    const laptopRow = screen.getByText("Yeni Dizüstü").closest("tr")!;
    expect(within(laptopRow).getByText(/Garanti bitiyor/)).toBeInTheDocument();
  });

  it("names every row action after its asset while keeping the visible text", async () => {
    permissions.granted = new Set(["assets.assets.view", "assets.assets.manage"]);
    renderPage();

    await screen.findByText("MacBook Pro");
    const history = screen.getAllByRole("button", { name: /^Atama geçmişi: / });
    expect(history.map((b) => b.getAttribute("aria-label"))).toEqual([
      "Atama geçmişi: MacBook Pro",
      "Atama geçmişi: iPhone 16",
    ]);
    for (const button of history) expect(button).toHaveTextContent(/^Atama geçmişi$/);
    expect(screen.getByRole("button", { name: "Düzenle: iPhone 16" })).toHaveTextContent(/^Düzenle$/);
  });

  it("reports an asset as lost with the date and note", async () => {
    permissions.granted = new Set(["assets.assets.view", "assets.assets.manage"]);
    const user = userEvent.setup();
    renderPage();

    const macRow = (await screen.findByText("MacBook Pro")).closest("tr")!;
    await user.click(within(macRow).getByRole("button", { name: "Kayıp bildir: MacBook Pro" }));
    const dialog = await screen.findByRole("dialog");
    expect(
      within(dialog).getByText(/çalışandaki atama kayıp nedeniyle kapanır/)
    ).toBeInTheDocument();
    await user.type(within(dialog).getByLabelText("Kayıp notu"), "Seyahatte çalındı");
    await user.click(within(dialog).getByRole("button", { name: "Kayıp olarak işaretle" }));

    await waitFor(() =>
      expect(markAssetLost).toHaveBeenCalledWith("as1", today(), "Seyahatte çalındı")
    );
    expect(toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "success" }));
  });

  it("returns an asset with condition and note", async () => {
    permissions.granted = new Set(["assets.assets.view", "assets.assets.manage"]);
    const user = userEvent.setup();
    renderPage();

    const macRow = (await screen.findByText("MacBook Pro")).closest("tr")!;
    await user.click(within(macRow).getByRole("button", { name: "İade al: MacBook Pro" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("combobox", { name: "İade durumu" }));
    await user.click(await screen.findByRole("option", { name: "Hasarlı", hidden: true }));
    await user.type(within(dialog).getByLabelText("İade notu"), "Ekran çatlak");
    await user.click(within(dialog).getByRole("button", { name: "İade al" }));

    await waitFor(() =>
      expect(returnAsset).toHaveBeenCalledWith("as1", today(), "damaged", "Ekran çatlak")
    );
  });

  it("returns with the default condition and no note when untouched", async () => {
    permissions.granted = new Set(["assets.assets.view", "assets.assets.manage"]);
    const user = userEvent.setup();
    renderPage();

    const macRow = (await screen.findByText("MacBook Pro")).closest("tr")!;
    await user.click(within(macRow).getByRole("button", { name: "İade al: MacBook Pro" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "İade al" }));

    await waitFor(() =>
      expect(returnAsset).toHaveBeenCalledWith("as1", today(), "good", undefined)
    );
  });

  it("opens the assignment history of an asset with the assignee names", async () => {
    permissions.granted = new Set(["assets.assets.view"]);
    const history: AssetAssignment[] = [
      { id: "h2", employeeId: "e1", issuedAt: "2026-09-01" },
      {
        id: "h1",
        employeeId: "e1",
        issuedAt: "2026-01-10",
        returnedAt: "2026-08-20",
        endReason: "returned",
        returnCondition: "damaged",
        note: "Menteşe kırık",
        acknowledgedAt: "2026-01-11T09:00:00Z",
      },
    ];
    vi.mocked(getAssetHistory).mockResolvedValue(history);
    const user = userEvent.setup();
    renderPage();

    const macRow = (await screen.findByText("MacBook Pro")).closest("tr")!;
    await user.click(within(macRow).getByRole("button", { name: "Atama geçmişi: MacBook Pro" }));

    const dialog = await screen.findByRole("dialog");
    expect(getAssetHistory).toHaveBeenCalledWith("as1");
    expect(await within(dialog).findAllByRole("link", { name: "Ayşe Demir" })).toHaveLength(2);
    expect(within(dialog).getByText("Zimmette")).toBeInTheDocument();
    expect(within(dialog).getByText(/devam ediyor/)).toBeInTheDocument();
    expect(within(dialog).getByText("İade edildi")).toBeInTheDocument();
    expect(within(dialog).getByText("İade durumu: Hasarlı")).toBeInTheDocument();
    expect(within(dialog).getByText("Not: Menteşe kırık")).toBeInTheDocument();
    expect(within(dialog).getByText(/Teslim alındı:/)).toBeInTheDocument();
    expect(within(dialog).getByText("Teslim onayı bekliyor")).toBeInTheDocument();
  });

  it("labels the create form fields so inputs are reachable by name", async () => {
    permissions.granted = new Set(["assets.assets.view", "assets.assets.manage"]);
    const user = userEvent.setup();
    renderPage();

    await screen.findByText("MacBook Pro");
    await user.click(screen.getByRole("button", { name: "Zimmet ekle" }));
    const dialog = await screen.findByRole("dialog");

    for (const label of [
      "Ad",
      "Demirbaş no",
      "Seri no",
      "Marka",
      "Model",
      "Alış tarihi",
      "Alış bedeli",
      "Garanti bitişi",
    ]) {
      expect(within(dialog).getByLabelText(new RegExp(`^${label}`))).toBeInTheDocument();
    }
  });
});

const CLOSED: EmployeeAssetHistoryItem = {
  id: "h1",
  employeeId: "e1",
  assetId: "as9",
  assetName: "Eski Laptop",
  categoryId: "c1",
  serialNumber: "OLD-1",
  issuedAt: "2025-01-10",
  returnedAt: "2025-12-31",
  endReason: "returned",
  returnCondition: "fair",
};
const LOST: EmployeeAssetHistoryItem = {
  id: "h3",
  employeeId: "e1",
  assetId: "as8",
  assetName: "Kayıp Telefon",
  categoryId: "c2",
  issuedAt: "2025-02-01",
  returnedAt: "2025-03-01",
  endReason: "lost",
};
const OPEN: EmployeeAssetHistoryItem = {
  id: "h2",
  employeeId: "e1",
  assetId: "as1",
  assetName: "MacBook Pro",
  categoryId: "c1",
  issuedAt: "2026-09-01",
};

describe("My assets v2", () => {
  it("lets the assignee confirm receipt only on assigned, unacknowledged assets", async () => {
    permissions.granted = new Set(["assets.my.assets"]);
    vi.mocked(getMyAssets).mockResolvedValue([
      INVENTORY[0]!,
      {
        ...INVENTORY[0]!,
        id: "as7",
        name: "Zimmet Telefon",
        acknowledgedAt: "2026-09-02T08:30:00Z",
      },
      { ...INVENTORY[0]!, id: "as3", name: "Bekleyen Monitör", status: "pendingAssignment" },
    ]);
    const user = userEvent.setup();
    renderPage();

    await screen.findByText("MacBook Pro");
    // Exactly one receipt button: for the assigned asset that has no confirmation yet.
    const buttons = screen.getAllByRole("button", { name: "Teslim aldım: MacBook Pro" });
    expect(buttons).toHaveLength(1);
    expect(screen.getByText(/Teslim alındı:/)).toBeInTheDocument();
    expect(screen.getByText("Atama onayında")).toBeInTheDocument();

    await user.click(buttons[0]!);

    await waitFor(() => expect(acknowledgeAsset).toHaveBeenCalledWith("as1"));
    expect(acknowledgeAsset).toHaveBeenCalledTimes(1);
    expect(toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "success", description: "Teslim onayınız kaydedildi." })
    );
  });

  it("explains how to recover when the receipt confirmation fails", async () => {
    permissions.granted = new Set(["assets.my.assets"]);
    vi.mocked(acknowledgeAsset).mockRejectedValueOnce(new Error("boom"));
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole("button", { name: "Teslim aldım: MacBook Pro" }));

    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({
          variant: "destructive",
          description: expect.stringContaining("tekrar deneyin"),
        })
      )
    );
  });

  it("lists only closed assignments under past assets, flagging lost ones", async () => {
    permissions.granted = new Set(["assets.my.assets"]);
    vi.mocked(getMyAssetHistory).mockResolvedValue([OPEN, LOST, CLOSED]);
    renderPage();

    expect(await screen.findByRole("heading", { name: "Geçmiş zimmetlerim" })).toBeInTheDocument();
    expect(await screen.findByText("Eski Laptop")).toBeInTheDocument();
    expect(screen.getByText("Kayıp Telefon")).toBeInTheDocument();
    expect(screen.getByText("İade durumu: Orta")).toBeInTheDocument();
    // "Kayıp" appears as the end reason badge of the lost assignment only.
    expect(screen.getAllByText("Kayıp")).toHaveLength(1);
    // The open assignment is shown above as a current asset, not repeated in the past list (one MacBook Pro card).
    expect(screen.getAllByText("MacBook Pro")).toHaveLength(1);
  });

  it("shows an empty state when nothing was ever returned", async () => {
    permissions.granted = new Set(["assets.my.assets"]);
    renderPage();

    expect(
      await screen.findByText("Daha önce iade ettiğiniz veya kayıp bildirilen zimmetiniz yok.")
    ).toBeInTheDocument();
  });
});

describe("feature switched off by the tenant", () => {
  it("shows the server message instead of a generic load error on my assets", async () => {
    permissions.granted = new Set(["assets.my.assets"]);
    vi.mocked(getMyAssets).mockRejectedValue(featureDisabled());
    renderPage();

    expect(await screen.findByText(FEATURE_DISABLED)).toBeInTheDocument();
    expect(screen.queryByText("Zimmetler yüklenemedi.")).not.toBeInTheDocument();
  });

  it("shows the server message in the past assets section", async () => {
    permissions.granted = new Set(["assets.my.assets"]);
    vi.mocked(getMyAssetHistory).mockRejectedValue(featureDisabled());
    renderPage();

    expect(await screen.findByText(FEATURE_DISABLED)).toBeInTheDocument();
    expect(screen.queryByText(/Geçmiş yüklenemedi/)).not.toBeInTheDocument();
  });

  it("falls back to the generic message for errors without a server detail", async () => {
    permissions.granted = new Set(["assets.my.assets"]);
    vi.mocked(getMyAssets).mockRejectedValue(new Error("network"));
    renderPage();

    expect(await screen.findByText("Zimmetler yüklenemedi.")).toBeInTheDocument();
  });
});
