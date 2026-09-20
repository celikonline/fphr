import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trAudit from "../../public/locales/tr/audit.json";
import trCommon from "../../public/locales/tr/common.json";
import AuditLogPage from "./audit-log";
import type { AuditEvent } from "@/services/audit.service";

// D-3 (ik-surec-testi-bulgu-raporu-2026-09-20): entity class names are shown localized (unknown ones stay raw) and the
// timestamp uses the same "medium" style as the rest of the app instead of the numeric TR locale string.
vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>(
    "@/components/permission-guard"
  );
  return { ...actual, usePermission: () => true };
});

const EVENTS: AuditEvent[] = [
  {
    id: "1",
    occurredAt: "2026-09-20T12:57:10Z",
    actor: "ik@demo.local",
    action: "create",
    resource: "OvertimeRequest",
    details: "OvertimeRequest Create",
    source: "Web",
  },
  {
    id: "2",
    occurredAt: "2026-09-20T12:58:10Z",
    actor: "ik@demo.local",
    action: "update",
    resource: "FutureEntity",
    details: "FutureEntity Update",
    source: "Web",
  },
];

vi.mock("@/services/audit.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/audit.service")>(
    "@/services/audit.service"
  );
  return {
    ...actual,
    listAuditEvents: vi.fn(async () => ({
      items: EVENTS,
      page: 1,
      pageSize: 20,
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
  Object.defineProperty(document, "fonts", {
    writable: true,
    value: { addEventListener: () => {}, removeEventListener: () => {} },
  });
});

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "audit",
    ns: ["audit", "common"],
    resources: { tr: { audit: trAudit, common: trCommon } },
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
          <AuditLogPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("AuditLogPage", () => {
  it("shows localized entity names and keeps an unknown entity's raw name", async () => {
    renderPage();

    expect(await screen.findByText("Fazla mesai talebi")).toBeTruthy();
    expect(screen.getByText("FutureEntity")).toBeTruthy();
    expect(screen.queryByText("OvertimeRequest")).toBeNull();
  });

  it("formats the timestamp in the app-wide medium date style", async () => {
    renderPage();

    await screen.findByText("Fazla mesai talebi");
    expect(screen.getAllByText(/20 Eyl 2026/).length).toBeGreaterThan(0);
    expect(screen.queryByText(/20\.09\.2026/)).toBeNull();
  });
});
