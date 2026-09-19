import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trDocuments from "../../public/locales/tr/documents.json";
import trCommon from "../../public/locales/tr/common.json";
import VisaRequestsPage from "./visa-requests";
import type { VisaProcess, VisaRequest } from "@/services/visa.service";

// senseik-001: the employee's own list shows each request's status; a rejected one must say so (with or without a
// reason - Workflow's rejection event carries none), and only a completed one offers the delivered documents.
vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return { ...actual, usePermission: () => true };
});

const REQUESTS: VisaRequest[] = [
  {
    id: "r1",
    employeeId: "e1",
    visaDocumentProcessId: "p1",
    travelStart: "2026-11-01T00:00:00Z",
    travelEnd: "2026-11-08T00:00:00Z",
    countryCode: "DE",
    visaType: "business",
    description: "Berlin",
    status: "awaitingApproval",
    createdAt: "2026-09-19T08:00:00Z",
  },
  {
    id: "r2",
    employeeId: "e1",
    travelStart: "2026-12-01T00:00:00Z",
    travelEnd: "2026-12-05T00:00:00Z",
    countryCode: "US",
    visaType: "tourist",
    description: "",
    status: "rejected",
    createdAt: "2026-09-10T08:00:00Z",
  },
  {
    id: "r3",
    employeeId: "e1",
    travelStart: "2026-10-01T00:00:00Z",
    travelEnd: "2026-10-03T00:00:00Z",
    countryCode: "GB",
    visaType: "work",
    description: "",
    status: "completed",
    createdAt: "2026-09-01T08:00:00Z",
  },
];

const PROCESSES: VisaProcess[] = [{ id: "p1", name: "Schengen vizesi", countryCode: null, isActive: true }];

vi.mock("@/services/visa.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/visa.service")>("@/services/visa.service");
  return {
    ...actual,
    listMyVisaRequests: vi.fn(async () => REQUESTS),
    listVisaProcesses: vi.fn(async () => PROCESSES),
    listVisaRequestDocuments: vi.fn(async () => []),
    submitVisaRequest: vi.fn(),
  };
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

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "documents",
    ns: ["documents", "common"],
    resources: { tr: { documents: trDocuments, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderPage(initialPath = "/app/requests/visa") {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter initialEntries={[initialPath]}>
            <VisaRequestsPage />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("VisaRequestsPage", () => {
  it("lists the caller's requests with a translated status, visa type and process name", async () => {
    renderPage();

    expect(await screen.findByText("Onay bekliyor")).toBeInTheDocument();
    expect(screen.getByText("Reddedildi")).toBeInTheDocument();
    expect(screen.getByText("Tamamlandı")).toBeInTheDocument();
    expect(screen.getByText("İş / ticari")).toBeInTheDocument();
    expect(screen.getByText("Schengen vizesi")).toBeInTheDocument();
  });

  it("explains a rejection that carries no reason and offers documents only for the completed request", async () => {
    renderPage();

    expect(await screen.findByText("Onaylayıcı talebi reddetti.")).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Belgeleri gör" })).toHaveLength(1);
  });

  it("opens the request form straight away when arriving from the requests hub with ?new=1", async () => {
    renderPage("/app/requests/visa?new=1");

    expect(await screen.findByText("Yeni vize belgesi talebi")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Talebi gönder" })).toBeInTheDocument();
  });
});
