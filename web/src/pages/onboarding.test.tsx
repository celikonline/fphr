import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { MemoryRouter } from "react-router";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trOnboarding from "../../public/locales/tr/onboarding.json";
import trCommon from "../../public/locales/tr/common.json";
import trEmployees from "../../public/locales/tr/employees.json";
import OnboardingPage from "./onboarding";
import {
  completeOnboardingTask,
  seedDefaultOnboardingTemplates,
  type OnboardingChecklistItem,
} from "@/services/onboarding.service";

const permissions = vi.hoisted(() => ({ granted: new Set<string>() }));

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>(
    "@/components/permission-guard"
  );
  return {
    ...actual,
    usePermission: (key: string) => permissions.granted.has(key),
    useAnyPermission: (keys: string[]) => keys.some((key) => permissions.granted.has(key)),
  };
});

const PENDING: OnboardingChecklistItem[] = [
  {
    id: "t1",
    employeeId: "e1",
    templateId: "tp1",
    title: "SGK işe giriş bildirgesinin verilmesi",
    responsibleRoleCode: "hr",
    isMandatory: true,
    stage: "onboarding",
    dueDate: "2026-09-20",
    status: "pending",
    isOverdue: true,
  },
  {
    id: "t2",
    employeeId: "e1",
    templateId: "tp2",
    title: "Zimmetlerin iade alınması",
    responsibleRoleCode: "it",
    isMandatory: false,
    stage: "offboarding",
    dueDate: "2026-09-30",
    status: "pending",
    isOverdue: false,
  },
];

vi.mock("@/services/onboarding.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/onboarding.service")>(
    "@/services/onboarding.service"
  );
  return {
    ...actual,
    listPendingOnboardingTasks: vi.fn(async () => PENDING),
    listOnboardingTemplates: vi.fn(async () => []),
    completeOnboardingTask: vi.fn(async () => {}),
    seedDefaultOnboardingTemplates: vi.fn(async () => 14),
  };
});

vi.mock("@/services/employees.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/employees.service")>(
    "@/services/employees.service"
  );
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
});

beforeEach(() => {
  vi.clearAllMocks();
});

function renderPage() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "onboarding",
    ns: ["onboarding", "common", "employees"],
    resources: { tr: { onboarding: trOnboarding, common: trCommon, employees: trEmployees } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter>
            <OnboardingPage />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("OnboardingPage", () => {
  it("lists pending tasks with the responsible party, flags overdue ones and filters by stage", async () => {
    permissions.granted = new Set(["onboarding.tasks.view"]);
    const user = userEvent.setup();
    renderPage();

    const sgkRow = (await screen.findByText("SGK işe giriş bildirgesinin verilmesi")).closest(
      "tr"
    )!;
    expect(within(sgkRow).getByText("İK")).toBeInTheDocument();
    expect(within(sgkRow).getByText("Zorunlu")).toBeInTheDocument();
    expect(screen.getByText("1 görevin süresi geçti")).toBeInTheDocument();
    expect(within(sgkRow).queryByRole("button", { name: "Tamamlandı" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("radio", { name: "İşten çıkış" }));
    expect(screen.queryByText("SGK işe giriş bildirgesinin verilmesi")).not.toBeInTheDocument();
    expect(screen.getByText("Zimmetlerin iade alınması")).toBeInTheDocument();
  });

  it("lets task managers complete a pending task", async () => {
    permissions.granted = new Set(["onboarding.tasks.view", "onboarding.tasks.manage"]);
    const user = userEvent.setup();
    renderPage();

    const sgkRow = (await screen.findByText("SGK işe giriş bildirgesinin verilmesi")).closest(
      "tr"
    )!;
    await user.click(within(sgkRow).getByRole("button", { name: "Tamamlandı" }));

    await waitFor(() => expect(completeOnboardingTask).toHaveBeenCalledWith("t1"));
  });

  it("offers the recommended templates when the tenant has none", async () => {
    permissions.granted = new Set(["onboarding.tasks.view", "onboarding.templates.manage"]);
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole("tab", { name: "Şablonlar" }));
    await user.click(await screen.findByRole("button", { name: "Önerilen şablonları yükle" }));

    await waitFor(() => expect(seedDefaultOnboardingTemplates).toHaveBeenCalled());
  });

  it("hides the templates tab from users who cannot manage templates", async () => {
    permissions.granted = new Set(["onboarding.tasks.view"]);
    renderPage();

    await screen.findByText("SGK işe giriş bildirgesinin verilmesi");
    expect(screen.queryByRole("tab", { name: "Şablonlar" })).not.toBeInTheDocument();
  });
});
