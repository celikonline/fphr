import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trOhs from "../../../public/locales/tr/ohs.json";
import trCommon from "../../../public/locales/tr/common.json";
import OhsRiskAssessmentsPage from "./ohs-risk-assessments";
import type { RiskAssessment } from "@/services/ohs.service";

// D-13 (ik-surec-testi-bulgu-raporu-2026-09-20): dates in the app-wide style instead of ISO, and an already-passed
// renewal date is flagged.
vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>(
    "@/components/permission-guard"
  );
  return { ...actual, usePermission: () => true };
});

const ASSESSMENTS: RiskAssessment[] = [
  {
    id: "a1",
    locationId: "loc-1",
    hazardClassAtAssessment: "low",
    assessmentDate: "2024-01-01",
    renewalPeriodMonths: 24,
    nextDueDate: "2026-02-01",
    preparedBy: "İSG Uzmanı",
    note: null,
  },
  {
    id: "a2",
    locationId: "loc-2",
    hazardClassAtAssessment: "medium",
    assessmentDate: "2026-03-01",
    renewalPeriodMonths: 24,
    nextDueDate: "2099-03-01",
    preparedBy: "İSG Uzmanı",
    note: null,
  },
];

vi.mock("@/services/ohs.service", async () => {
  const actual =
    await vi.importActual<typeof import("@/services/ohs.service")>("@/services/ohs.service");
  return { ...actual, listRiskAssessments: vi.fn(async () => ASSESSMENTS) };
});
vi.mock("@/services/locations.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/locations.service")>(
    "@/services/locations.service"
  );
  return {
    ...actual,
    listLocations: vi.fn(async () => ({
      items: [
        { id: "loc-1", name: "İzmir Office" },
        { id: "loc-2", name: "Ankara Office" },
      ],
      page: 1,
      pageSize: 200,
      totalCount: 2,
      totalPages: 1,
      hasNext: false,
      hasPrevious: false,
    })),
  };
});

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
    defaultNS: "ohs",
    ns: ["ohs", "common"],
    resources: { tr: { ohs: trOhs, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <OhsRiskAssessmentsPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("OhsRiskAssessmentsPage", () => {
  it("shows dates in the app-wide style rather than ISO", async () => {
    renderPage();

    expect(await screen.findByText("1 Oca 2024")).toBeTruthy();
    expect(screen.getByText("1 Şub 2026")).toBeTruthy();
    expect(screen.queryByText("2024-01-01")).toBeNull();
  });

  it("flags only the renewal date that has already passed", async () => {
    renderPage();

    await screen.findByText("1 Şub 2026");
    expect(screen.getAllByText(trOhs.riskAssessments.overdue)).toHaveLength(1);
  });
});
