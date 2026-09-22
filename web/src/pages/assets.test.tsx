import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAssets from "../../public/locales/tr/assets.json";
import trCommon from "../../public/locales/tr/common.json";
import trEmployees from "../../public/locales/tr/employees.json";
import AssetsPage from "./assets";
import { listAssets, retireAsset, type Asset } from "@/services/assets.service";

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

vi.mock("@/services/assets.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/assets.service")>(
    "@/services/assets.service"
  );
  return {
    ...actual,
    listAssets: vi.fn(async () => ({
      items: INVENTORY,
      page: 1,
      pageSize: 20,
      totalCount: 2,
      totalPages: 1,
      hasNext: false,
      hasPrevious: false,
    })),
    getMyAssets: vi.fn(async () => [INVENTORY[0]]),
    listAssetCategories: vi.fn(async () => [
      { id: "c1", code: "LAPTOP", name: "Dizüstü" },
      { id: "c2", code: "PHONE", name: "Telefon" },
    ]),
    retireAsset: vi.fn(async () => {}),
  };
});

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
});

beforeEach(() => {
  vi.clearAllMocks();
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
    expect(within(macRow).getByRole("button", { name: "İade al" })).toBeInTheDocument();
    expect(
      within(macRow).queryByRole("button", { name: "Kullanım dışı bırak" })
    ).not.toBeInTheDocument();
    expect(within(phoneRow).getByRole("button", { name: "Zimmet ata" })).toBeInTheDocument();

    await user.click(within(phoneRow).getByRole("button", { name: "Kullanım dışı bırak" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Kullanım dışı bırak" }));

    expect(retireAsset).toHaveBeenCalledWith("as2");
  });
});
