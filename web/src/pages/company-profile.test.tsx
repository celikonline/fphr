import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trCompany from "../../public/locales/tr/company.json";
import trCommon from "../../public/locales/tr/common.json";
import CompanyProfilePage from "./company-profile";
import type { CompanyProfile } from "@/services/company.service";

// TK-007: company-profile.tsx artık localStorage yerine gerçek Organization API'sine
// (GET/PUT /v1/organization/companies/profile) bağlı; bu test onu doğrular.
const permissions = vi.hoisted(() => ({ granted: new Set<string>(["identity.tenant.manage"]) }));

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>(
    "@/components/permission-guard"
  );
  return { ...actual, usePermission: (key: string) => permissions.granted.has(key) };
});

const baseProfile: CompanyProfile = {
  id: "company-1",
  name: "Acme A.Ş.",
  registrationNumber: "1234567",
  taxNumber: "9876543210",
  address: "Maslak, İstanbul",
  city: "İstanbul",
  country: "Türkiye",
  operatingHours: { start: "09:00", end: "18:00" },
  workingDays: ["mon", "tue", "wed", "thu", "fri"],
  updatedAt: "2026-09-01T00:00:00Z",
};

const getCompanyProfile = vi.fn(async () => baseProfile);
const updateCompanyProfile = vi.fn(async (patch: Partial<CompanyProfile>) => ({
  ...baseProfile,
  ...patch,
}));

vi.mock("@/services/company.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/company.service")>(
    "@/services/company.service"
  );
  return {
    ...actual,
    getCompanyProfile: () => getCompanyProfile(),
    updateCompanyProfile: (patch: Partial<CompanyProfile>) => updateCompanyProfile(patch),
  };
});

vi.mock("@/services/payroll.service", () => ({ getActiveIncentiveSummary: vi.fn(async () => []) }));

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
    defaultNS: "company",
    ns: ["company", "common"],
    resources: { tr: { company: trCompany, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <CompanyProfilePage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("CompanyProfilePage", () => {
  it("loads the profile from the Organization API instead of localStorage", async () => {
    const setItemSpy = vi.spyOn(window.localStorage.__proto__, "setItem");

    renderPage();

    expect(await screen.findByDisplayValue("Acme A.Ş.")).toBeInTheDocument();
    expect(getCompanyProfile).toHaveBeenCalledTimes(1);
    expect(setItemSpy).not.toHaveBeenCalled();

    setItemSpy.mockRestore();
  });

  it("submits edits through updateCompanyProfile with the API's field shape", async () => {
    const user = userEvent.setup();
    renderPage();

    const nameInput = await screen.findByDisplayValue("Acme A.Ş.");
    await user.clear(nameInput);
    await user.type(nameInput, "Acme Yazılım A.Ş.");

    const saveButton = screen.getByRole("button", { name: trCommon.save });
    await user.click(saveButton);

    await waitFor(() => expect(updateCompanyProfile).toHaveBeenCalledTimes(1));
    expect(updateCompanyProfile).toHaveBeenCalledWith(
      expect.objectContaining({
        name: "Acme Yazılım A.Ş.",
        registrationNumber: baseProfile.registrationNumber,
        taxNumber: baseProfile.taxNumber,
        operatingHours: baseProfile.operatingHours,
        workingDays: baseProfile.workingDays,
      })
    );
  });
});
