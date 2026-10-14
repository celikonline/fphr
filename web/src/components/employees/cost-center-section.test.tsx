import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import trEmployees from "../../../public/locales/tr/employees.json";
import trCommon from "../../../public/locales/tr/common.json";
import trSettings from "../../../public/locales/tr/settings.json";
import { CostCenterSection } from "./cost-center-section";
import { assignEmployeeCostCenter, type EmployeeDetail } from "@/services/employees.service";
import { getCostCenters } from "@/services/organization.service";

const permissions = vi.hoisted(() => ({ granted: new Set<string>() }));
vi.mock("@/components/permission-guard", () => ({
  usePermission: (key: string) => permissions.granted.has(key),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));
vi.mock("@/services/employees.service", () => ({ assignEmployeeCostCenter: vi.fn() }));
vi.mock("@/services/organization.service", () => ({ getCostCenters: vi.fn() }));

const employee = { id: "director", titleName: "İnsan Kaynakları Direktörü" } as EmployeeDetail;
const center = { id: "hr-center", code: "10200", name: "İnsan Kaynakları", isActive: true };

afterEach(cleanup);

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener() {},
      removeListener() {},
      addEventListener() {},
      removeEventListener() {},
      dispatchEvent() {
        return false;
      },
    }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
  Element.prototype.scrollIntoView = () => {};
});

beforeEach(() => {
  vi.resetAllMocks();
  permissions.granted = new Set(["employee.employees.update", "organization.cost_centers.view"]);
  vi.mocked(getCostCenters).mockResolvedValue([center]);
  vi.mocked(assignEmployeeCostCenter).mockResolvedValue({
    ...employee,
    costCenterId: center.id,
    costCenterCode: center.code,
    costCenterName: center.name,
    costCenterIsActive: true,
  });
});

function renderSection(detail = employee) {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    resources: { tr: { employees: trEmployees, common: trCommon, settings: trSettings } },
    interpolation: { escapeValue: false },
  });
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={client}>
        <MantineProvider env="test">
          <CostCenterSection employee={detail} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
  return client;
}

describe("employee cost center", () => {
  it("assigns 10200 by code and name using only the dedicated assignment request", async () => {
    const user = userEvent.setup();
    const client = renderSection();
    expect(getCostCenters).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "Düzenle" }));
    const select = screen.getByRole("combobox", { name: "Maliyet merkezi" });
    await waitFor(() => expect(select).toBeEnabled());
    await user.click(select);
    await user.click(await screen.findByRole("option", { name: "10200 — İnsan Kaynakları" }));
    await user.click(screen.getByRole("button", { name: "Kaydet" }));
    await waitFor(() =>
      expect(assignEmployeeCostCenter).toHaveBeenCalledWith("director", "hr-center")
    );
    await waitFor(() =>
      expect(
        client.getQueryData<EmployeeDetail>(["employees", "detail", "director"])?.costCenterId
      ).toBe("hr-center")
    );
  });

  it("clears an existing assignment explicitly", async () => {
    const user = userEvent.setup();
    renderSection({
      ...employee,
      costCenterId: center.id,
      costCenterCode: center.code,
      costCenterName: center.name,
    });
    await user.click(screen.getByRole("button", { name: "Düzenle" }));
    const select = screen.getByRole("combobox", { name: "Maliyet merkezi" });
    await waitFor(() => expect(select).toBeEnabled());
    await user.click(select);
    await user.click(await screen.findByRole("option", { name: "Maliyet merkezi atanmamış" }));
    await user.click(screen.getByRole("button", { name: "Kaydet" }));
    await waitFor(() => expect(assignEmployeeCostCenter).toHaveBeenCalledWith("director", null));
  });

  it("keeps a deactivated current center visible and excludes other inactive centers", async () => {
    const user = userEvent.setup();
    vi.mocked(getCostCenters).mockResolvedValue([
      center,
      { id: "inactive-other", code: "10000", name: "Genel Müdürlük", isActive: false },
    ]);
    renderSection({
      ...employee,
      costCenterId: "old",
      costCenterCode: "10100",
      costCenterName: "Muhasebe",
      costCenterIsActive: false,
    });
    expect(screen.getByText("10100 — Muhasebe")).toBeInTheDocument();
    expect(screen.getByText("Pasif")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Düzenle" }));
    const select = screen.getByRole("combobox", { name: "Maliyet merkezi" });
    await waitFor(() => expect(select).toBeEnabled());
    await user.click(select);
    expect(await screen.findByRole("option", { name: "10100 — Muhasebe (Pasif)" })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: /Genel Müdürlük/ })).not.toBeInTheDocument();
  });

  it("prevents saving when the catalog fails and offers retry", async () => {
    const user = userEvent.setup();
    vi.mocked(getCostCenters).mockRejectedValue(new Error("offline"));
    renderSection();
    await user.click(screen.getByRole("button", { name: "Düzenle" }));
    expect(await screen.findByText("Maliyet merkezleri yüklenemedi.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Kaydet" })).toBeDisabled();
    vi.mocked(getCostCenters).mockResolvedValue([center]);
    await user.click(screen.getByRole("button", { name: "Yeniden dene" }));
    await waitFor(() =>
      expect(screen.getByRole("combobox", { name: "Maliyet merkezi" })).toBeEnabled()
    );
    expect(assignEmployeeCostCenter).not.toHaveBeenCalled();
  });

  it.each(["employee.employees.update", "organization.cost_centers.view"])(
    "does not edit without %s",
    (permission) => {
      permissions.granted.delete(permission);
      renderSection();
      expect(screen.queryByRole("button", { name: "Düzenle" })).not.toBeInTheDocument();
      expect(getCostCenters).not.toHaveBeenCalled();
    }
  );
});
