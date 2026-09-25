import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trCommon from "../../../public/locales/tr/common.json";
import trEmployees from "../../../public/locales/tr/employees.json";
import EmployeeMultiPicker from "./employee-multi-picker";

vi.mock("@/services/employees.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/employees.service")>("@/services/employees.service");
  return { ...actual, listEmployees: vi.fn(), listOrgUnitOptions: vi.fn().mockResolvedValue([]) };
});
import { listEmployees } from "@/services/employees.service";

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

function person(id: string, name: string) {
  return { id, displayName: name, firstName: name, lastName: "", status: "Active" };
}
const THREE = [person("a", "Ayşe"), person("b", "Bora"), person("c", "Can")];

function page(items: ReturnType<typeof person>[], totalCount = items.length) {
  return { items, totalCount, page: 1, pageSize: 50 } as never;
}

function renderPicker(onConfirm = vi.fn()) {
  const i18n = i18next.createInstance();
  void i18n.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "common",
    ns: ["common", "employees"],
    resources: { tr: { common: trCommon, employees: trEmployees } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <EmployeeMultiPicker onConfirm={onConfirm} />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
  return onConfirm;
}

const confirmButton = () => screen.getByRole("button", { name: trEmployees.multiPicker.confirm });

describe("EmployeeMultiPicker", () => {
  beforeEach(() => {
    vi.mocked(listEmployees).mockReset();
    vi.mocked(listEmployees).mockResolvedValue(page(THREE));
  });

  it("emits_EmployeeSelection_with_ids", async () => {
    const user = userEvent.setup();
    const onConfirm = renderPicker();
    await user.click(await screen.findByRole("checkbox", { name: "Ayşe" }));
    await user.click(screen.getByRole("checkbox", { name: "Can" }));
    await user.click(confirmButton());
    expect(onConfirm).toHaveBeenCalledWith({ employeeIds: ["a", "c"], filter: null, excludedEmployeeIds: null });
  });

  it("select_all_with_exclusions_emits_filter_and_excluded", async () => {
    const user = userEvent.setup();
    vi.mocked(listEmployees).mockResolvedValue(page(THREE, 120));
    const onConfirm = renderPicker();
    await screen.findByRole("checkbox", { name: "Ayşe" });
    await user.click(screen.getByRole("button", { name: /120/ }));
    await user.click(screen.getByRole("checkbox", { name: "Bora" }));
    await user.click(confirmButton());
    const selection = onConfirm.mock.calls[0]![0];
    expect(selection.employeeIds).toBeNull();
    expect(selection.excludedEmployeeIds).toEqual(["b"]);
    expect(selection.filter).toMatchObject({ includeTerminated: null });
  });

  it("keeps_selected_when_filter_changes", async () => {
    const user = userEvent.setup();
    const onConfirm = renderPicker();
    await user.click(await screen.findByRole("checkbox", { name: "Ayşe" }));
    vi.mocked(listEmployees).mockResolvedValue(page([person("d", "Deniz")]));
    await user.type(screen.getByRole("textbox", { name: trEmployees.multiPicker.search }), "De");
    await screen.findByRole("checkbox", { name: "Deniz" });
    await user.click(screen.getByRole("checkbox", { name: "Deniz" }));
    await user.click(confirmButton());
    expect(onConfirm.mock.calls[0]![0].employeeIds).toEqual(["a", "d"]);
  });

  it("terminated_toggle_sets_includeTerminated", async () => {
    const user = userEvent.setup();
    renderPicker();
    await screen.findByRole("checkbox", { name: "Ayşe" });
    await user.click(screen.getByRole("switch", { name: trEmployees.multiPicker.includeTerminated }));
    await waitFor(() =>
      expect(vi.mocked(listEmployees).mock.calls.slice(-1)[0]![0]).toMatchObject({ includeTerminated: true }),
    );
  });

  it("empty_result_disables_confirm", async () => {
    vi.mocked(listEmployees).mockResolvedValue(page([]));
    renderPicker();
    await screen.findByText(trEmployees.multiPicker.empty);
    expect(confirmButton()).toBeDisabled();
  });
});
