import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import type { ReactNode } from "react";
import trOnboarding from "../../../public/locales/tr/onboarding.json";
import trCommon from "../../../public/locales/tr/common.json";
import OnboardingCategories from "./onboarding-categories";
import OnboardingTemplateSets, { moveItem } from "./onboarding-template-sets";
import OnboardingTemplates from "./onboarding-templates";
import { ImportanceChip } from "./importance-chip";
import OnboardingPage from "@/pages/onboarding";
import * as service from "@/services/onboarding.service";

const permissions = vi.hoisted(() => ({ granted: new Set<string>() }));

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>(
    "@/components/permission-guard"
  );
  return { ...actual, usePermission: (key: string) => permissions.granted.has(key) };
});

const toastSpy = vi.hoisted(() => vi.fn());
vi.mock("@/hooks/use-toast", () => ({ toast: toastSpy }));

vi.mock("@/components/employees/employee-picker", () => ({ default: () => null }));
vi.mock("@/components/employees/employee-onboarding-card", () => ({
  EmployeeOnboardingCard: () => null,
}));

vi.mock("@/services/onboarding.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/onboarding.service")>(
    "@/services/onboarding.service"
  );
  return {
    ...actual,
    listOnboardingTemplates: vi.fn(),
    listOnboardingCategories: vi.fn(),
    listOnboardingTemplateSets: vi.fn(),
    listPendingOnboardingTasks: vi.fn().mockResolvedValue([]),
    createOnboardingTemplate: vi.fn().mockResolvedValue({}),
    createOnboardingTemplateSet: vi.fn().mockResolvedValue({}),
    deleteOnboardingCategory: vi.fn(),
  };
});

const templates = [
  {
    id: "t1",
    stage: "onboarding",
    title: "SGK girişi",
    responsibleRoleCode: "hr",
    dueDateOffsetDays: 0,
    isMandatory: true,
    isActive: true,
    importance: "critical",
    categoryId: "c1",
  },
  {
    id: "t2",
    stage: "onboarding",
    title: "Laptop teslimi",
    responsibleRoleCode: "it",
    dueDateOffsetDays: 1,
    isMandatory: false,
    isActive: true,
    importance: "normal",
  },
  {
    id: "t3",
    stage: "offboarding",
    title: "Zimmet iadesi",
    responsibleRoleCode: "it",
    dueDateOffsetDays: 0,
    isMandatory: false,
    isActive: true,
    importance: "normal",
  },
];

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
  window.HTMLElement.prototype.scrollIntoView = () => {};
});

beforeEach(() => {
  permissions.granted = new Set(["onboarding.tasks.view", "onboarding.templates.manage"]);
  vi.mocked(service.listOnboardingTemplates).mockResolvedValue(templates as never);
  vi.mocked(service.listOnboardingCategories).mockResolvedValue([
    { id: "c1", name: "Belgeler", sortOrder: 0, isActive: true },
  ]);
  vi.mocked(service.listOnboardingTemplateSets).mockResolvedValue([]);
  vi.mocked(service.createOnboardingTemplate).mockClear();
  vi.mocked(service.createOnboardingTemplateSet).mockClear();
});

function wrap(ui: ReactNode) {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "onboarding",
    ns: ["onboarding", "common"],
    resources: { tr: { onboarding: trOnboarding, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>{ui}</MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("onboarding management", () => {
  it("template_form_sends_importance_and_edocument", async () => {
    const user = userEvent.setup();
    wrap(<OnboardingTemplates />);
    const addButtons = await screen.findAllByRole("button", { name: "Görev ekle" });
    await user.click(addButtons[0]);
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText(/Görev/, { selector: "input" }), "Yeni görev");
    await user.click(within(dialog).getByLabelText("Önem"));
    await user.click(await screen.findByRole("option", { name: "Yüksek", hidden: true }));
    await user.click(within(dialog).getByLabelText("E-evrak gerekli"));
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));
    await waitFor(() =>
      expect(service.createOnboardingTemplate).toHaveBeenCalledWith(
        expect.objectContaining({
          title: "Yeni görev",
          importance: "high",
          requiresEDocument: true,
        })
      )
    );
  });

  it("category_delete_in_use_shows_409_message", async () => {
    const detail = "Kategori bir veya daha fazla görev şablonunda kullanıldığı için silinemez.";
    const error = Object.assign(new Error("conflict"), {
      isAxiosError: true,
      response: { status: 409, data: { code: "onboarding.category_in_use", detail } },
    });
    vi.mocked(service.deleteOnboardingCategory).mockRejectedValue(error);
    const user = userEvent.setup();
    wrap(<OnboardingCategories />);
    await screen.findByText("Belgeler");
    await user.click(screen.getByRole("button", { name: "Sil" }));
    const dialog = await screen.findByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: "Sil" }));
    await waitFor(() =>
      expect(toastSpy).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "destructive", description: detail })
      )
    );
  });

  it("category_delete_asks_for_confirmation_first", async () => {
    vi.mocked(service.deleteOnboardingCategory).mockResolvedValue(undefined as never);
    const user = userEvent.setup();
    wrap(<OnboardingCategories />);
    await screen.findByText("Belgeler");
    await user.click(screen.getByRole("button", { name: "Sil" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/"Belgeler" kategorisi silinsin mi\?/)).toBeInTheDocument();
    expect(service.deleteOnboardingCategory).not.toHaveBeenCalled();
    await user.click(within(dialog).getByRole("button", { name: "Sil" }));
    await waitFor(() => expect(service.deleteOnboardingCategory).toHaveBeenCalled());
    expect(vi.mocked(service.deleteOnboardingCategory).mock.calls[0]![0]).toBe("c1");
  });

  it("set_order_is_preserved_on_save", async () => {
    expect(moveItem(["a", "b", "c"], 0, 1)).toEqual(["b", "a", "c"]);
    const user = userEvent.setup();
    wrap(<OnboardingTemplateSets />);
    await user.click(await screen.findByRole("button", { name: "Set ekle" }));
    const dialog = await screen.findByRole("dialog");
    await user.type(within(dialog).getByLabelText(/Set adı/), "Mavi yaka");
    await user.click(within(dialog).getByLabelText("SGK girişi"));
    await user.click(within(dialog).getByLabelText("Laptop teslimi"));
    await user.click(
      within(dialog).getByRole("button", { name: "Laptop teslimi görevini yukarı taşı" })
    );
    await user.click(within(dialog).getByRole("button", { name: "Kaydet" }));
    await waitFor(() =>
      expect(service.createOnboardingTemplateSet).toHaveBeenCalledWith({
        stage: "onboarding",
        name: "Mavi yaka",
        templateIds: ["t2", "t1"],
      })
    );
  });

  it("set_requires_templates_of_same_stage", async () => {
    const user = userEvent.setup();
    wrap(<OnboardingTemplateSets />);
    await user.click(await screen.findByRole("button", { name: "Set ekle" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).queryByLabelText("Zimmet iadesi")).toBeNull();
    expect(
      (within(dialog).getByRole("button", { name: "Kaydet" }) as HTMLButtonElement).disabled
    ).toBe(true);
    await user.click(within(dialog).getByText("İşten çıkış"));
    expect(await within(dialog).findByLabelText("Zimmet iadesi")).toBeTruthy();
    expect(within(dialog).queryByLabelText("SGK girişi")).toBeNull();
  });

  it("importance_chip_has_text", () => {
    wrap(<ImportanceChip importance="critical" />);
    expect(screen.getByText("Kritik")).toBeTruthy();
  });

  it("manage_hidden_without_permission", async () => {
    permissions.granted = new Set(["onboarding.tasks.view"]);
    wrap(<OnboardingPage />);
    await screen.findByRole("tab", { name: "Açık görevler" }).catch(() => null);
    expect(screen.queryByRole("tab", { name: "Kategoriler" })).toBeNull();
    expect(screen.queryByRole("tab", { name: "Şablon setleri" })).toBeNull();
    expect(screen.queryByRole("tab", { name: "Şablonlar" })).toBeNull();
  });
});
