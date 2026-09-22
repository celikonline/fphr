import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trOhs from "../../../public/locales/tr/ohs.json";
import trCommon from "../../../public/locales/tr/common.json";
import OhsServicesPanel from "./ohs-services";

const permissions = vi.hoisted(() => ({ granted: new Set<string>() }));

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>(
    "@/components/permission-guard"
  );
  return { ...actual, usePermission: (key: string) => permissions.granted.has(key) };
});

vi.mock("@/services/ohs-services.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/ohs-services.service")>(
    "@/services/ohs-services.service"
  );
  return {
    ...actual,
    listLocationServiceSummaries: vi.fn(async () => [
      {
        locationId: "loc1",
        hazardClass: "high",
        employeeCount: 60,
        minutes: [
          { role: "safetyExpert", requiredMinutes: 2400, assignedMinutes: 1000, isMet: false },
          { role: "workplacePhysician", requiredMinutes: 900, assignedMinutes: 900, isMet: true },
        ],
        committeeRequired: true,
        committeeMeetingIntervalMonths: 1,
        committeeMemberCount: 1,
        requiredRepresentativeCount: 2,
        representativeCount: 0,
        issues: ["insufficient_minutes_safety_expert", "committee_missing"],
      },
    ]),
    listSafetyAssignments: vi.fn(async () => [
      {
        id: "a1",
        locationId: "loc1",
        role: "safetyExpert",
        personName: "Deniz Uzman",
        certificateClass: "b",
        providerName: "Güven OSGB",
        monthlyMinutes: 1000,
        startDate: "2026-01-01",
      },
    ]),
    listCommitteeMembers: vi.fn(async () => []),
  };
});

// jsdom has no matchMedia/ResizeObserver; Mantine's Card/Table need these to render.
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

function renderPanel() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "ohs",
    ns: ["ohs", "common"],
    resources: { tr: { ohs: trOhs, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const locations = {
    options: [{ value: "loc1", label: "Fabrika" }],
    nameOf: (id?: string | null) => (id === "loc1" ? "Fabrika" : "—"),
  };
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <OhsServicesPanel locations={locations} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("OhsServicesPanel", () => {
  it("shows the location's statutory gaps and the assignment register", async () => {
    permissions.granted = new Set(["ohs.services.view"]);
    renderPanel();

    expect(await screen.findByText("1000 / 2400 dk/ay")).toBeInTheDocument();
    expect(screen.getByText(trOhs.services.issues.committee_missing)).toBeInTheDocument();
    expect(await screen.findByText("Deniz Uzman")).toBeInTheDocument();
    expect(screen.getByText("Güven OSGB")).toBeInTheDocument();
    expect(screen.getByText(trOhs.services.noMembers)).toBeInTheDocument();
    expect(screen.queryByText(trOhs.services.addAssignment)).not.toBeInTheDocument();
  });

  it("offers management actions with the manage permission", async () => {
    permissions.granted = new Set(["ohs.services.view", "ohs.services.manage"]);
    renderPanel();

    expect(await screen.findByText(trOhs.services.addAssignment)).toBeInTheDocument();
    expect(await screen.findByText(trOhs.services.end)).toBeInTheDocument();
  });
});
