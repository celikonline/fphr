import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import employees from "../../../public/locales/tr/employees.json";
import common from "../../../public/locales/tr/common.json";
import { PersonnelNumberSection } from "./personnel-number-section";
import { getPersonnelNumberAssignment } from "@/services/personnel-numbers.service";
import { updateEmployee, type EmployeeDetail } from "@/services/employees.service";
vi.mock("@/components/permission-guard", () => ({ usePermission: () => true }));
vi.mock("@/services/personnel-numbers.service", async (original) => ({
  ...(await original<typeof import("@/services/personnel-numbers.service")>()),
  getPersonnelNumberAssignment: vi.fn(),
}));
vi.mock("@/services/employees.service", async (original) => ({
  ...(await original<typeof import("@/services/employees.service")>()),
  updateEmployee: vi.fn(),
}));
beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }),
  });
});
beforeEach(() => {
  vi.clearAllMocks();
});
function renderSection(employee: Partial<EmployeeDetail>) {
  const i18n = i18next.createInstance();
  void i18n.init({
    lng: "tr",
    resources: { tr: { employees, common } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  render(
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider
        client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
      >
        <MantineProvider>
          <PersonnelNumberSection
            employee={{ id: "e1", orgUnitId: "o1", ...employee } as EmployeeDetail}
          />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}
describe("PersonnelNumberSection", () => {
  it("shows immutable main number and every legacy reference without an assignment request", () => {
    renderSection({
      sicilNumarasi: "00000123",
      legacyPersonnelNumbers: [
        { sourceSystem: "SAP", personnelNumber: "00000123" },
        { sourceSystem: "EskiERP", personnelNumber: "ABC-123" },
      ],
    });
    expect(screen.getByText("00000123")).toBeInTheDocument();
    expect(screen.getByText("EskiERP: ABC-123")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: employees.personnelNumbers.assign })
    ).not.toBeInTheDocument();
    expect(getPersonnelNumberAssignment).not.toHaveBeenCalled();
  });
  it("assigns an empty existing card internally without submitting a preview number", async () => {
    vi.mocked(getPersonnelNumberAssignment).mockResolvedValue({
      configured: true,
      assignmentMode: "internal",
      nextNumber: "00000124",
    });
    vi.mocked(updateEmployee).mockResolvedValue({
      id: "e1",
      sicilNumarasi: "00000125",
    } as EmployeeDetail);
    renderSection({});
    await waitFor(() =>
      expect(screen.getByRole("button", { name: employees.personnelNumbers.assign })).toBeEnabled()
    );
    await userEvent.click(screen.getByRole("button", { name: employees.personnelNumbers.assign }));
    await waitFor(() =>
      expect(updateEmployee).toHaveBeenCalledWith(
        "e1",
        expect.objectContaining({ sicilNumarasi: null })
      )
    );
  });
  it("keeps assignment disabled on preview failure", async () => {
    vi.mocked(getPersonnelNumberAssignment).mockRejectedValue(new Error("offline"));
    renderSection({});
    await screen.findByRole("button", { name: employees.personnelNumbers.retry });
    expect(screen.getByRole("button", { name: employees.personnelNumbers.assign })).toBeDisabled();
    expect(updateEmployee).not.toHaveBeenCalled();
  });
});
