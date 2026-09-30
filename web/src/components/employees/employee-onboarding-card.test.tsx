import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trEmployees from "../../../public/locales/tr/employees.json";
import trOnboarding from "../../../public/locales/tr/onboarding.json";
import trCommon from "../../../public/locales/tr/common.json";
import { EmployeeOnboardingCard } from "./employee-onboarding-card";
import { startOnboardingProcess } from "@/services/onboarding.service";

const permissions = vi.hoisted(() => ({ granted: new Set<string>() }));

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>(
    "@/components/permission-guard"
  );
  return { ...actual, usePermission: (key: string) => permissions.granted.has(key) };
});

vi.mock("@/services/onboarding.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/onboarding.service")>(
    "@/services/onboarding.service"
  );
  return {
    ...actual,
    getEmployeeCategoryProgress: vi.fn().mockResolvedValue([
      { categoryId: "c1", name: "Belgeler", done: 1, total: 3, percent: 33 },
      { name: "Genel", done: 2, total: 2, percent: 100 },
    ]),
    listOnboardingTemplateSets: vi.fn().mockResolvedValue([
      { id: "s1", stage: "onboarding", name: "Mavi yaka", templateIds: [], isActive: true },
      { id: "s2", stage: "onboarding", name: "Yönetici", templateIds: [], isActive: true },
      { id: "s3", stage: "onboarding", name: "Pasif set", templateIds: [], isActive: false },
    ]),
    startOnboardingProcess: vi.fn().mockResolvedValue(undefined),
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

function renderCard() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "employees",
    ns: ["employees", "onboarding", "common"],
    resources: { tr: { employees: trEmployees, onboarding: trOnboarding, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <EmployeeOnboardingCard employeeId="emp-1" />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("EmployeeOnboardingCard", () => {
  beforeEach(() => {
    permissions.granted = new Set(["onboarding.tasks.view", "onboarding.tasks.manage"]);
    vi.mocked(startOnboardingProcess).mockClear();
  });

  it("category_progress_rendered", async () => {
    renderCard();
    expect(await screen.findByText("Belgeler")).toBeTruthy();
    expect(screen.getByText("1/3")).toBeTruthy();
    expect(screen.getByText("2/2")).toBeTruthy();
  });

  it("start_process_picks_named_set", async () => {
    const user = userEvent.setup();
    renderCard();
    const select = (await screen.findByLabelText("Şablon seti")) as HTMLSelectElement;
    await waitFor(() => expect(select.querySelectorAll("option").length).toBeGreaterThan(2));
    expect(screen.queryByText("Pasif set")).toBeNull();
    await user.selectOptions(select, "s2");
    await user.click(screen.getByRole("button", { name: "Süreci başlat" }));
    await waitFor(() =>
      expect(startOnboardingProcess).toHaveBeenCalledWith("emp-1", "s2", expect.any(String))
    );
  });

  it("hides the start form without manage permission", async () => {
    permissions.granted = new Set(["onboarding.tasks.view"]);
    renderCard();
    await screen.findByText("Belgeler");
    expect(screen.queryByRole("button", { name: "Süreci başlat" })).toBeNull();
  });
});
