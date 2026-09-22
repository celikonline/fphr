import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trOhs from "../../public/locales/tr/ohs.json";
import trCommon from "../../public/locales/tr/common.json";
import trEmployees from "../../public/locales/tr/employees.json";
import OhsPage from "./ohs";
import { reportNearMiss, type OhsIncident } from "@/services/ohs-incidents.service";

const permissions = vi.hoisted(() => ({ granted: new Set<string>() }));

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return {
    ...actual,
    usePermission: (key: string) => permissions.granted.has(key),
    useAnyPermission: (keys: string[]) => keys.some((key) => permissions.granted.has(key)),
  };
});

const INCIDENTS: OhsIncident[] = [
  {
    id: "i1",
    kind: "workAccident",
    eventDate: "2026-09-15",
    employeeId: "e1",
    description: "Forklift ayağına çarptı",
    sgkNotificationDeadline: "2026-09-18",
    isSgkOverdue: true,
  },
  {
    id: "i2",
    kind: "workAccident",
    eventDate: "2026-09-10",
    employeeId: "e1",
    description: "Merdivenden kaydı",
    sgkNotificationDeadline: "2026-09-15",
    sgkNotifiedOn: "2026-09-11",
    isSgkOverdue: false,
  },
  {
    id: "i3",
    kind: "nearMiss",
    eventDate: "2026-09-12",
    reportedByEmployeeId: "e1",
    description: "Islak zemin",
    isSgkOverdue: false,
    correctiveAction: "Uyarı levhası kondu",
    closedOn: "2026-09-13",
  },
];

vi.mock("@/services/ohs-incidents.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/ohs-incidents.service")>(
    "@/services/ohs-incidents.service"
  );
  return {
    ...actual,
    listIncidents: vi.fn(async () => INCIDENTS),
    listMyIncidentReports: vi.fn(async () => [INCIDENTS[2]]),
    reportNearMiss: vi.fn(async () => "new-id"),
  };
});

vi.mock("@/services/employees.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/employees.service")>("@/services/employees.service");
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
  // Mantine's autosize Textarea listens for font loading; jsdom has no FontFaceSet.
  Object.defineProperty(document, "fonts", {
    configurable: true,
    value: { addEventListener: () => {}, removeEventListener: () => {}, ready: Promise.resolve() },
  });
});

beforeEach(() => {
  vi.clearAllMocks();
});

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "ohs",
    ns: ["ohs", "common", "employees"],
    resources: { tr: { ohs: trOhs, common: trCommon, employees: trEmployees } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter>
            <OhsPage />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("OhsPage", () => {
  it("flags an overdue SGK notification and only offers closing after the accident was notified", async () => {
    permissions.granted = new Set(["ohs.incidents.view", "ohs.incidents.manage"]);
    renderPage();

    const overdueRow = (await screen.findByText("Forklift ayağına çarptı")).closest("tr")!;
    expect(within(overdueRow).getByText(/Süre geçti/)).toBeInTheDocument();
    expect(within(overdueRow).getByRole("button", { name: "SGK'ya bildirildi" })).toBeInTheDocument();
    expect(within(overdueRow).getByRole("button", { name: "Kapat" })).toBeDisabled();

    const notifiedRow = screen.getByText("Merdivenden kaydı").closest("tr")!;
    expect(within(notifiedRow).getByText(/Bildirildi/)).toBeInTheDocument();
    expect(within(notifiedRow).getByRole("button", { name: "Kapat" })).toBeEnabled();

    const closedRow = screen.getByText("Islak zemin").closest("tr")!;
    expect(within(closedRow).getByText("Kapandı")).toBeInTheDocument();
    expect(within(closedRow).queryByRole("button")).not.toBeInTheDocument();
  });

  it("summarises missed SGK deadlines and hides recording from read-only users", async () => {
    permissions.granted = new Set(["ohs.incidents.view"]);
    renderPage();

    await screen.findByText("Forklift ayağına çarptı");
    const overdueCard = screen.getByText("SGK süresi geçen").parentElement!;
    expect(within(overdueCard).getByText("1")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Olay kaydet" })).not.toBeInTheDocument();
  });

  it("lets an employee report a near miss and see the action taken on earlier reports", async () => {
    permissions.granted = new Set(["ohs.my.incident_reports"]);
    const user = userEvent.setup();
    renderPage();

    expect(await screen.findByText(/Uyarı levhası kondu/)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Ramak kala bildir" }));
    await user.type(await screen.findByLabelText(/Olayın anlatımı/), "Depo girişinde açık kablo");
    const dialog = screen.getByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Bildir" }));

    await waitFor(() =>
      expect(reportNearMiss).toHaveBeenCalledWith(expect.objectContaining({ description: "Depo girişinde açık kablo" }))
    );
  });
});
