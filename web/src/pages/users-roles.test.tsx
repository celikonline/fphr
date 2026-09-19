import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { I18nextProvider, useTranslation } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trUsers from "../../public/locales/tr/users.json";
import trCommon from "../../public/locales/tr/common.json";
import { RolesTab } from "./users-roles";
import { listPermissionGroups, listRoles, type PermissionGroup, type Role } from "@/services/users.service";

// senseik-040: permission display names and role names must reach the "Kullanıcılar → Roller" screen already
// localized to Turkish (RolesController/PermissionsController resolve them server-side via SharedResource.resx),
// so this only has to verify the page renders whatever the API sends - no client-side translation lookup for
// these values.
vi.mock("@/services/users.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/users.service")>("@/services/users.service");
  return { ...actual, listRoles: vi.fn(), listPermissionGroups: vi.fn() };
});

// jsdom has no matchMedia/ResizeObserver; Mantine's Card/ScrollArea.Autosize/NavLink need these to render.
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

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "users",
    ns: ["users", "common"],
    resources: { tr: { users: trUsers, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function RolesTabHarness() {
  const { t } = useTranslation("users");
  return <RolesTab t={t} />;
}

function renderRolesTab() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <RolesTabHarness />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

function hrRole(): Role {
  return {
    id: "role-hr",
    code: "HR",
    // As returned by RolesController.Localize (roles.hr.name) - matches the same resource key MeController now
    // resolves for "Profilim" (senseik-040 consistency fix), not the "İK" shorthand the old frontend map used.
    name: "İK Yöneticisi",
    description: "Özlük, izin, PDKS/puantaj, belge, duyuru, eğitim, zimmet, performans, ücret, işe alım, raporlar, KVKK.",
    isSystem: true,
    isLocked: false,
    userCount: 3,
    permissions: [
      { permission: "employee.salary.edit", scope: "company", scopeIds: null },
      { permission: "leave.my.requests", scope: "self", scopeIds: null },
    ],
  };
}

function permissionCatalog(): PermissionGroup[] {
  return [
    {
      module: "employee",
      group: "Özlük",
      permissions: [
        { key: "employee.salary.edit", module: "employee", displayName: "Maaş Düzenleme", group: "Özlük", description: null },
      ],
    },
    {
      module: "leave",
      group: "İzin",
      permissions: [
        {
          key: "leave.my.requests",
          module: "leave",
          displayName: "Kendi İzin Taleplerimi Yönetme",
          group: "İzin",
          description: null,
        },
      ],
    },
  ];
}

describe("RolesTab (senseik-040 permission/role i18n)", () => {
  it("renders the role name and its permission list in Turkish", async () => {
    vi.mocked(listRoles).mockResolvedValue([hrRole()]);
    vi.mocked(listPermissionGroups).mockResolvedValue(permissionCatalog());

    renderRolesTab();

    // findBy* rejects (failing the test) if the element never appears - awaiting it is the presence assertion.
    const heading = await screen.findByRole("heading", { name: "İK Yöneticisi" });
    expect(heading.textContent).toBe("İK Yöneticisi");
    expect((await screen.findByText("Maaş Düzenleme")).textContent).toBe("Maaş Düzenleme");
    expect((await screen.findByText("Kendi İzin Taleplerimi Yönetme")).textContent).toBe("Kendi İzin Taleplerimi Yönetme");

    // No raw permission key or leftover English label should ever reach the screen.
    expect(screen.queryByText("employee.salary.edit")).toBeNull();
    expect(screen.queryByText(/Edit Salary/i)).toBeNull();
  });

  it("falls back to the raw permission key only when the catalog has no match (never blank)", async () => {
    vi.mocked(listRoles).mockResolvedValue([hrRole()]);
    vi.mocked(listPermissionGroups).mockResolvedValue([]);

    renderRolesTab();

    expect((await screen.findByText("employee.salary.edit")).textContent).toBe("employee.salary.edit");
  });
});
