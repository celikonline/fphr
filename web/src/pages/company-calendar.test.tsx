import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trCompanyCalendar from "../../public/locales/tr/companyCalendar.json";
import trCommon from "../../public/locales/tr/common.json";
import trEmployees from "../../public/locales/tr/employees.json";
import CompanyCalendarPage from "./company-calendar";
import { useAuthStore } from "@/store/auth.store";
import { toDateOnly } from "@/lib/dates";
import { getCompanyCalendar, updateCompanyEvent, type CompanyCalendarEntry } from "@/services/company-calendar.service";

// TK-015: web-side edit for manual (companyEvent/meeting) calendar entries; system-generated entries
// (birthday/leaveStart) stay read-only, matching the existing delete-action restriction.
vi.mock("@/services/company-calendar.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/company-calendar.service")>(
    "@/services/company-calendar.service"
  );
  return { ...actual, getCompanyCalendar: vi.fn(), updateCompanyEvent: vi.fn(async () => {}) };
});

// The org-unit filter/visibility picker is unrelated to edit behavior; stub it out so tests do not depend on a
// network call (same approach as leave-calendar.test.tsx).
vi.mock("@/components/employees/use-org-units", () => ({
  useOrgUnits: () => ({ options: [], nameById: new Map(), isLoading: false }),
}));

// jsdom has no matchMedia/ResizeObserver/document.fonts; Mantine's Modal/Tooltip/Textarea need one of these.
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
  // Mantine's autosize Textarea (description field) listens for font loading; jsdom has no FontFaceSet.
  Object.defineProperty(document, "fonts", {
    configurable: true,
    value: { addEventListener: () => {}, removeEventListener: () => {}, ready: Promise.resolve() },
  });
});

// The real "now" (no fake timers: they hang RTL's findBy/waitFor polling). Fixture entries are dated on it so
// they always land inside the default-rendered month, regardless of which day the test happens to run on.
const TODAY_ISO = toDateOnly(new Date());

const MANUAL_ENTRY: CompanyCalendarEntry = {
  id: "evt-1",
  title: "Yıl Sonu Kutlaması",
  description: "Tüm şirket davetli",
  date: TODAY_ISO,
  type: "companyEvent",
  visibilityOrgUnitId: null,
};

const SYSTEM_ENTRY: CompanyCalendarEntry = {
  id: null,
  title: "Ayşe Yılmaz doğum günü",
  date: TODAY_ISO,
  type: "birthday",
  employeeId: "emp-1",
};

beforeEach(() => {
  useAuthStore.setState({ hasPermission: () => true, isAccessSettingEnabled: () => true });
  vi.mocked(getCompanyCalendar).mockResolvedValue([MANUAL_ENTRY, SYSTEM_ENTRY]);
});

afterEach(() => {
  vi.mocked(getCompanyCalendar).mockReset();
  vi.mocked(updateCompanyEvent).mockReset();
});

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "companyCalendar",
    ns: ["companyCalendar", "common", "employees"],
    resources: { tr: { companyCalendar: trCompanyCalendar, common: trCommon, employees: trEmployees } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <CompanyCalendarPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("CompanyCalendarPage edit (TK-015)", () => {
  it("shows an edit action only for the manual event, not the system-generated birthday entry", async () => {
    renderPage();

    await screen.findByText("Yıl Sonu Kutlaması");
    expect(screen.getByText("Ayşe Yılmaz doğum günü")).toBeInTheDocument();

    expect(screen.getAllByRole("button", { name: "edit" })).toHaveLength(1);
  });

  it("opens the edit dialog pre-filled with the entry's current values", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole("button", { name: "edit" }));

    expect(await screen.findByRole("heading", { name: "Etkinliği Düzenle" })).toBeInTheDocument();
    // The Başlık label carries an extra " *" (required marker) as a nested span, so match by prefix.
    expect(screen.getByLabelText(/^Başlık/)).toHaveValue("Yıl Sonu Kutlaması");
    expect(screen.getByLabelText("Açıklama")).toHaveValue("Tüm şirket davetli");
    expect(screen.getByRole("radio", { name: "Şirket Etkinliği" })).toBeChecked();
  });

  it("submits the edited fields to updateCompanyEvent with the entry's id", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole("button", { name: "edit" }));
    const titleInput = await screen.findByLabelText(/^Başlık/);
    await user.clear(titleInput);
    await user.type(titleInput, "Yeni Yıl Kutlaması");

    await user.click(screen.getByRole("button", { name: "Kaydet" }));

    await vi.waitFor(() =>
      expect(updateCompanyEvent).toHaveBeenCalledWith("evt-1", {
        title: "Yeni Yıl Kutlaması",
        description: "Tüm şirket davetli",
        eventDate: TODAY_ISO,
        type: "companyEvent",
        visibilityOrgUnitId: null,
      })
    );
  });
});
