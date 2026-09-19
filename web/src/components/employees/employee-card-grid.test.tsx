import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MantineProvider } from "@mantine/core";
import trEmployees from "../../../public/locales/tr/employees.json";
import EmployeeCardGrid from "./employee-card-grid";
import type { EmployeeListItem } from "@/services/employees.service";

// jsdom has no matchMedia/ResizeObserver/document.fonts; Mantine's Tooltip and other components need one of these
// mounted to render without throwing (mirrors web/src/components/leave/leave-type-dialog.test.tsx).
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
    defaultNS: "employees",
    ns: ["employees"],
    resources: { tr: { employees: trEmployees } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderGrid(items: EmployeeListItem[]) {
  return render(
    <I18nextProvider i18n={createTestI18n()}>
      <MantineProvider>
        <EmployeeCardGrid items={items} emptyMessage="empty" unitNames={new Map()} onOpen={vi.fn()} />
      </MantineProvider>
    </I18nextProvider>,
  );
}

function baseEmployee(overrides: Partial<EmployeeListItem>): EmployeeListItem {
  return {
    id: "emp-1",
    firstName: "Ada",
    lastName: "Yılmaz",
    displayName: "Ada Yılmaz",
    nationalIdMasked: null,
    workEmail: "ada@example.com",
    personalEmail: null,
    workPhone: null,
    status: "Active",
    orgUnitId: null,
    managerEmployeeId: null,
    managerDisplayName: null,
    userId: null,
    accountStatus: "none",
    hireDate: "2020-01-01",
    contractType: null,
    employmentType: null,
    hasMissingInfo: false,
    missingInfoFieldCount: 0,
    ...overrides,
  };
}

// senseik-045: the "Eksik bilgi" badge used to be a flat yes/no with no count, so every incomplete record read
// identically regardless of how much was actually missing. It must now show the count, and it must stay hidden
// entirely for a complete record.
describe("EmployeeCardGrid missing-info badge", () => {
  it("shows the missing-field count in the badge text when hasMissingInfo is true", () => {
    renderGrid([baseEmployee({ hasMissingInfo: true, missingInfoFieldCount: 2 })]);

    expect(screen.getByText("Eksik bilgi (2)")).not.toBeNull();
  });

  it("reflects a different count for a different employee", () => {
    renderGrid([baseEmployee({ id: "emp-2", hasMissingInfo: true, missingInfoFieldCount: 5 })]);

    expect(screen.getByText("Eksik bilgi (5)")).not.toBeNull();
  });

  it("does not render the badge when hasMissingInfo is false", () => {
    renderGrid([baseEmployee({ hasMissingInfo: false, missingInfoFieldCount: 0 })]);

    expect(screen.queryByText(/Eksik bilgi/)).toBeNull();
  });
});
