import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import settings from "../../../public/locales/tr/settings.json";
import common from "../../../public/locales/tr/common.json";
import PersonnelNumbersPage from "./personnel-numbers";
import * as service from "@/services/personnel-numbers.service";
const permissions = vi.hoisted(() => ({ manage: true }));
vi.mock("@/components/permission-guard", () => ({ usePermission: () => permissions.manage }));
vi.mock("@/components/no-access", () => ({ default: () => <div>No access</div> }));
vi.mock("@/services/personnel-numbers.service", async (original) => ({
  ...(await original<typeof service>()),
  getPersonnelNumberRanges: vi.fn(),
  getPersonnelNumberRules: vi.fn(),
  getPersonnelNumberCompanies: vi.fn(),
  savePersonnelNumberRange: vi.fn(),
  savePersonnelNumberRule: vi.fn(),
  setPersonnelNumberRangeActive: vi.fn(),
}));
const range = {
  id: "r1",
  code: "01",
  name: "Yeni işe alım",
  startNumber: "10000000",
  endNumber: "19999999",
  assignmentMode: "internal" as const,
  isActive: true,
};
beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: (media: string) => ({
      matches: false,
      media,
      addListener() {},
      removeListener() {},
      addEventListener() {},
      removeEventListener() {},
      dispatchEvent: () => false,
    }),
  });
  window.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
  Element.prototype.scrollIntoView = vi.fn();
});
beforeEach(() => {
  vi.clearAllMocks();
  permissions.manage = true;
  vi.mocked(service.getPersonnelNumberRanges).mockResolvedValue([range]);
  vi.mocked(service.getPersonnelNumberRules).mockResolvedValue([]);
  vi.mocked(service.getPersonnelNumberCompanies).mockResolvedValue([
    { id: "c1", name: "Şirket A", personnelAreas: [{ id: "a1", name: "Ankara" }] },
  ]);
});
function renderPage() {
  const i18n = i18next.createInstance();
  void i18n.init({
    lng: "tr",
    resources: { tr: { settings, common } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider
        client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
      >
        <MantineProvider>
          <PersonnelNumbersPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}
describe("PersonnelNumbersPage", () => {
  it("requires management permission before reading configuration", () => {
    permissions.manage = false;
    renderPage();
    expect(screen.getByText("No access")).toBeInTheDocument();
    expect(service.getPersonnelNumberRanges).not.toHaveBeenCalled();
  });
  it("locks code, start and assignment mode and rejects shortening a range", async () => {
    renderPage();
    const user = userEvent.setup();
    await user.click(await screen.findByRole("button", { name: common.edit }));
    expect(screen.getByLabelText(new RegExp(settings.personnelNumbers.code))).toBeDisabled();
    expect(screen.getByLabelText(new RegExp(settings.personnelNumbers.start))).toBeDisabled();
    expect(
      screen.getByRole("checkbox", { name: settings.personnelNumbers.external })
    ).toBeDisabled();
    fireEvent.change(screen.getByLabelText(new RegExp(settings.personnelNumbers.end)), {
      target: { value: "10000001" },
    });
    await user.click(screen.getByRole("button", { name: common.save }));
    expect(screen.getByText(settings.personnelNumbers.invalidRange)).toBeInTheDocument();
    expect(service.savePersonnelNumberRange).not.toHaveBeenCalled();
  });
  it("chooses company and optional personnel area for a new rule", async () => {
    renderPage();
    const user = userEvent.setup();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: settings.personnelNumbers.addRule })).toBeEnabled()
    );
    await user.click(screen.getByRole("button", { name: settings.personnelNumbers.addRule }));
    await user.click(screen.getByRole("combobox", { name: /Şirket \(BUKRS\)/ }));
    await user.click(await screen.findByText("Şirket A"));
    await user.click(
      screen.getByRole("combobox", { name: settings.personnelNumbers.personnelArea })
    );
    await user.click(await screen.findByText("Ankara"));
    await user.click(screen.getByRole("combobox", { name: new RegExp(settings.personnelNumbers.operation) }));
    await user.click(await screen.findByText(settings.personnelNumbers.import));
    await user.click(
      screen.getByRole("combobox", { name: new RegExp(`^${settings.personnelNumbers.range}`) })
    );
    await user.click(
      await screen.findByText(`01 · Yeni işe alım · ${settings.personnelNumbers.internal}`)
    );
    await user.click(screen.getByRole("button", { name: common.save }));
    await waitFor(() =>
      expect(service.savePersonnelNumberRule).toHaveBeenCalledWith(
        {
          companyId: "c1",
          personnelAreaId: "a1",
          operation: "import",
          rangeId: "r1",
          isActive: true,
        },
        undefined
      )
    );
  });
  it("shows loading failures with a retry action", async () => {
    vi.mocked(service.getPersonnelNumberRanges).mockRejectedValue(new Error("offline"));
    renderPage();
    expect(
      await screen.findByRole("button", { name: settings.personnelNumbers.retry })
    ).toBeInTheDocument();
  });
});
