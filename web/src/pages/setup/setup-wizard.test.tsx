import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter, Route, Routes } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trSetup from "../../../public/locales/tr/setup.json";
import trCommon from "../../../public/locales/tr/common.json";
import trSettings from "../../../public/locales/tr/settings.json";
import SetupWizardPage from "./setup-wizard";
import { getCompanyProfile, updateCompanyProfile } from "@/services/company.service";
import { createLocation, listLocations } from "@/services/locations.service";
import { getModuleSettings, updateModuleSettings } from "@/services/module-settings.service";
import {
  assignCompanyHolidayCalendar,
  createUnit,
  listCompanies,
  listHolidayCalendars,
  listUnitTypes,
  listUnits,
} from "@/services/structure.service";
import { completeTenantSetup, ensureOrganization, getTenantProfile, inviteUser, updateTenantProfile } from "@/services/tenant-setup.service";
import { listRoles } from "@/services/users.service";
import { useAuthStore } from "@/store/auth.store";

vi.mock("@/services/tenant-setup.service", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/tenant-setup.service")>()),
  getTenantProfile: vi.fn(),
  ensureOrganization: vi.fn(),
  updateTenantProfile: vi.fn(),
  completeTenantSetup: vi.fn(),
  inviteUser: vi.fn(),
}));
vi.mock("@/services/company.service", () => ({ getCompanyProfile: vi.fn(), updateCompanyProfile: vi.fn() }));
vi.mock("@/services/structure.service", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/structure.service")>()),
  listCompanies: vi.fn(),
  listHolidayCalendars: vi.fn(),
  assignCompanyHolidayCalendar: vi.fn(),
  listUnitTypes: vi.fn(),
  listUnits: vi.fn(),
  createUnit: vi.fn(),
}));
vi.mock("@/services/locations.service", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/locations.service")>()),
  listLocations: vi.fn(),
  createLocation: vi.fn(),
}));
vi.mock("@/services/module-settings.service", () => ({ getModuleSettings: vi.fn(), updateModuleSettings: vi.fn() }));
vi.mock("@/services/users.service", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/users.service")>()),
  listRoles: vi.fn(),
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
  // Mantine's Select/Combobox scroll the active option into view; jsdom doesn't implement it.
  Element.prototype.scrollIntoView = vi.fn();
});

const refreshMe = vi.fn();

function setUser(setupCompleted: boolean) {
  useAuthStore.setState({
    token: null,
    user: {
      id: "u1",
      email: "boss@acme.test",
      name: "Ali Veli",
      roles: ["Owner"],
      roleLabels: ["Owner"],
      permissions: ["identity.tenant.manage"],
      tenant: { id: "t1", name: "Acme A.Ş.", slug: "acme", setupCompleted },
    },
    features: { access: {} as never, accessSettingsBypass: true, mfaSetupRequired: false, disabledModules: [] },
    refreshMe,
  });
}

function renderWizard() {
  const i18n = i18next.createInstance();
  void i18n.init({
    lng: "tr",
    fallbackLng: "tr",
    ns: ["setup", "common", "settings"],
    resources: { tr: { setup: trSetup, common: trCommon, settings: trSettings } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter initialEntries={["/app/setup"]}>
            <Routes>
              <Route path="/app" element={<div>Home page</div>} />
              <Route path="/app/setup" element={<SetupWizardPage />} />
            </Routes>
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

function stubBackend() {
  vi.mocked(getTenantProfile).mockResolvedValue({
    id: "t1",
    slug: "acme",
    name: "Acme A.Ş.",
    status: "Trial",
    locale: "tr-TR",
    timeZone: "Europe/Istanbul",
    setupCompleted: false,
  });
  vi.mocked(ensureOrganization).mockResolvedValue(undefined);
  vi.mocked(updateTenantProfile).mockResolvedValue({} as never);
  vi.mocked(completeTenantSetup).mockResolvedValue(undefined);
  vi.mocked(inviteUser).mockResolvedValue("new-user");
  vi.mocked(getCompanyProfile).mockResolvedValue({
    id: "c1",
    name: "Acme A.Ş.",
    registrationNumber: "",
    taxNumber: "",
    address: "",
    city: "",
    country: "",
    operatingHours: { start: "09:00", end: "18:00" },
    workingDays: ["mon", "tue", "wed", "thu", "fri"],
    updatedAt: "2026-09-20T10:00:00Z",
  });
  vi.mocked(updateCompanyProfile).mockResolvedValue({} as never);
  vi.mocked(listCompanies).mockResolvedValue([
    { id: "co1", orgUnitId: "ou1", name: "Acme A.Ş.", holidayCalendarId: "cal1", jointEmploymentGroupCode: null },
  ]);
  vi.mocked(listHolidayCalendars).mockResolvedValue([
    { id: "cal1", name: "Türkiye Resmi Tatilleri", countryCode: "TR", isSystemDefined: true },
  ]);
  vi.mocked(assignCompanyHolidayCalendar).mockResolvedValue({} as never);
  vi.mocked(listLocations).mockResolvedValue({ items: [], page: 1, pageSize: 1, totalCount: 0, totalPages: 0, hasNext: false, hasPrevious: false });
  vi.mocked(createLocation).mockResolvedValue({} as never);
  vi.mocked(listUnitTypes).mockResolvedValue([
    { id: "ty-company", code: "Company", name: "Şirket", sortOrder: 0, isActive: true, isSystem: true, allowsNesting: false },
    { id: "ty-dept", code: "Department", name: "Departman", sortOrder: 2, isActive: true, isSystem: true, allowsNesting: true },
  ]);
  vi.mocked(listUnits).mockResolvedValue([
    { id: "ou1", typeId: "ty-company", parentId: null, name: "Acme A.Ş.", managerEmployeeId: null, locationId: null, sortOrder: 0, isActive: true },
  ]);
  vi.mocked(createUnit).mockResolvedValue({} as never);
  vi.mocked(getModuleSettings).mockResolvedValue({
    modules: [
      { key: "employee", isCore: true, enabled: true },
      { key: "leave", isCore: false, enabled: true },
      { key: "payroll", isCore: false, enabled: true },
    ],
    disabledModules: [],
  });
  vi.mocked(updateModuleSettings).mockResolvedValue(undefined);
  vi.mocked(listRoles).mockResolvedValue([
    { id: "r-owner", code: "Owner", name: "Owner", description: null, isSystem: true, isLocked: true, userCount: 1, permissions: [] },
    { id: "r-hr", code: "HR", name: "İK Uzmanı", description: null, isSystem: true, isLocked: false, userCount: 0, permissions: [] },
  ]);
}

const NEXT = trSetup.actions.next;

async function completeCompanyStep(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByLabelText(/^Şirket adı/);
  await user.click(screen.getByRole("button", { name: NEXT }));
  await screen.findByText(trSetup.calendar.heading);
}

describe("SetupWizardPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    stubBackend();
    setUser(false);
  });

  it("sends people with nothing to set up straight to the app", async () => {
    setUser(true);
    renderWizard();

    expect(await screen.findByText("Home page")).toBeInTheDocument();
    expect(getTenantProfile).not.toHaveBeenCalled();
  });

  it("prepares the organization, then saves the company details before letting the owner continue", async () => {
    const user = userEvent.setup();
    renderWizard();

    const nameInput = await screen.findByLabelText(/^Şirket adı/);
    expect(ensureOrganization).toHaveBeenCalledWith("Acme A.Ş.");

    await user.clear(nameInput);
    await user.type(nameInput, "Acme Teknoloji A.Ş.");
    await user.type(screen.getByLabelText(/^Vergi numarası/), "1234567890");
    await user.click(screen.getByRole("button", { name: NEXT }));

    await screen.findByText(trSetup.calendar.heading);
    expect(updateTenantProfile).toHaveBeenCalledWith({
      name: "Acme Teknoloji A.Ş.",
      taxNumber: "1234567890",
      locale: "tr-TR",
      timeZone: "Europe/Istanbul",
    });
    expect(updateCompanyProfile).toHaveBeenCalledWith(
      expect.objectContaining({
        name: "Acme Teknoloji A.Ş.",
        taxNumber: "1234567890",
        operatingHours: { start: "09:00", end: "18:00" },
        workingDays: ["mon", "tue", "wed", "thu", "fri"],
      }),
    );
    expect(refreshMe).toHaveBeenCalled();
  });

  it("requires a company name and does not call the API without one", async () => {
    const user = userEvent.setup();
    renderWizard();

    const nameInput = await screen.findByLabelText(/^Şirket adı/);
    await user.clear(nameInput);
    await user.click(screen.getByRole("button", { name: NEXT }));

    expect(await screen.findByRole("alert")).toHaveTextContent(trSetup.errors.nameRequired);
    expect(updateTenantProfile).not.toHaveBeenCalled();
  });

  it("shows a load error when the company cannot be prepared", async () => {
    vi.mocked(ensureOrganization).mockRejectedValue(new Error("boom"));
    renderWizard();

    expect(await screen.findByRole("alert")).toHaveTextContent(trSetup.errors.load);
  });

  it("walks every step: location, departments, modules and an invitation, then finishes setup", async () => {
    const user = userEvent.setup();
    renderWizard();
    await completeCompanyStep(user);

    // Step 2: the system holiday calendar is already assigned; add the head location.
    await user.type(await screen.findByLabelText(/^Lokasyon adı/), "Merkez Ofis");
    await user.click(screen.getByRole("button", { name: NEXT }));
    await screen.findByText(trSetup.structure.heading);
    expect(createLocation).toHaveBeenCalledWith({ name: "Merkez Ofis", type: "office", countryCode: "TR", address: null });
    expect(assignCompanyHolidayCalendar).not.toHaveBeenCalled();

    // Step 3: departments under the root company unit, in the order typed.
    await user.type(await screen.findByLabelText(/^Yeni departmanlar/), "Finans{Enter}Satış");
    await user.click(screen.getByRole("button", { name: NEXT }));
    await screen.findByRole("heading", { name: trSetup.modules.heading });
    expect(createUnit).toHaveBeenCalledTimes(2);
    expect(createUnit).toHaveBeenNthCalledWith(1, { typeId: "ty-dept", parentId: "ou1", name: "Finans", managerEmployeeId: null, locationId: null });
    expect(createUnit).toHaveBeenNthCalledWith(2, { typeId: "ty-dept", parentId: "ou1", name: "Satış", managerEmployeeId: null, locationId: null });

    // Step 4: switch a module off, invite an HR user, finish.
    await user.click(await screen.findByRole("switch", { name: "Bordro" }));

    await user.type(screen.getByLabelText("Ad"), "Ayşe");
    await user.type(screen.getByLabelText("Soyad"), "Kaya");
    await user.type(screen.getByLabelText("E-posta"), "hr@acme.test");
    await user.click(screen.getAllByLabelText("Rol")[0]);
    await user.click(await screen.findByRole("option", { name: "İK Uzmanı", hidden: true }));
    await user.click(screen.getByRole("button", { name: trSetup.modules.invite }));
    expect(await screen.findByText("Davet gönderildi: hr@acme.test")).toBeInTheDocument();
    expect(inviteUser).toHaveBeenCalledWith({ email: "hr@acme.test", firstName: "Ayşe", lastName: "Kaya", roleIds: ["r-hr"], language: "tr" });

    await user.click(screen.getByRole("button", { name: trSetup.actions.finish }));

    expect(await screen.findByText("Home page")).toBeInTheDocument();
    expect(updateModuleSettings).toHaveBeenCalledWith(["payroll"]);
    expect(completeTenantSetup).toHaveBeenCalledTimes(1);
    // /me must be refreshed AFTER the completion call, otherwise the guard would still see setupCompleted=false.
    const refreshOrders = refreshMe.mock.invocationCallOrder;
    expect(refreshOrders[refreshOrders.length - 1]).toBeGreaterThan(vi.mocked(completeTenantSetup).mock.invocationCallOrder[0]);
  });

  it("lets optional steps be skipped without saving anything", async () => {
    const user = userEvent.setup();
    renderWizard();
    await completeCompanyStep(user);

    await user.click(await screen.findByRole("button", { name: trSetup.actions.skip }));
    await screen.findByText(trSetup.structure.heading);
    await user.click(screen.getByRole("button", { name: trSetup.actions.skip }));
    await screen.findByRole("heading", { name: trSetup.modules.heading });

    expect(createLocation).not.toHaveBeenCalled();
    expect(createUnit).not.toHaveBeenCalled();
  });

  it("refuses an invitation without a role and never calls the API", async () => {
    const user = userEvent.setup();
    renderWizard();
    await completeCompanyStep(user);
    await user.click(await screen.findByRole("button", { name: trSetup.actions.skip }));
    await user.click(await screen.findByRole("button", { name: trSetup.actions.skip }));

    await user.type(await screen.findByLabelText("Ad"), "Ayşe");
    await user.type(screen.getByLabelText("Soyad"), "Kaya");
    await user.type(screen.getByLabelText("E-posta"), "hr@acme.test");
    await user.click(screen.getByRole("button", { name: trSetup.modules.invite }));

    await waitFor(() => expect(screen.getByText(trSetup.modules.invalid)).toBeInTheDocument());
    expect(inviteUser).not.toHaveBeenCalled();
  });

  it("keeps the owner on the last step and shows an error when finishing fails", async () => {
    vi.mocked(completeTenantSetup).mockRejectedValue(new Error("boom"));
    const user = userEvent.setup();
    renderWizard();
    await completeCompanyStep(user);
    await user.click(await screen.findByRole("button", { name: trSetup.actions.skip }));
    await user.click(await screen.findByRole("button", { name: trSetup.actions.skip }));

    await user.click(await screen.findByRole("button", { name: trSetup.actions.finish }));

    expect(await screen.findByRole("alert")).toHaveTextContent(trSetup.errors.finish);
    expect(screen.queryByText("Home page")).not.toBeInTheDocument();
  });
});
