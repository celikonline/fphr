import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { AxiosError, type AxiosResponse } from "axios";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trEmployees from "../../../public/locales/tr/employees.json";
import trCommon from "../../../public/locales/tr/common.json";
import CreateEmployeeDialog from "./create-employee-dialog";
import { createEmployee } from "@/services/employees.service";

// N-2 (ik-surec-testi-bulgu-raporu-2026-09-20): a duplicate work e-mail must be reported on the "İş e-postası"
// field, not only in the toast that disappears after a few seconds.
vi.mock("@/services/employees.service", () => ({ createEmployee: vi.fn() }));
vi.mock("./org-unit-select", () => ({ default: () => null }));
vi.mock("./employee-picker", () => ({ default: () => null }));

const DUPLICATE_MESSAGE = "Bu iş e-postası başka bir çalışanda zaten kayıtlı.";

function conflict(code: string, detail: string): AxiosError {
  return new AxiosError("Conflict", "ERR_BAD_REQUEST", undefined, undefined, {
    status: 409,
    data: { code, detail, status: 409 },
  } as AxiosResponse);
}

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
    defaultNS: "employees",
    ns: ["employees", "common"],
    resources: { tr: { employees: trEmployees, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderDialog() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <MemoryRouter>
            <CreateEmployeeDialog open onOpenChange={vi.fn()} />
          </MemoryRouter>
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("CreateEmployeeDialog", () => {
  it("shows a duplicate work e-mail on the work e-mail field and clears it once edited", async () => {
    vi.mocked(createEmployee).mockRejectedValueOnce(
      conflict("employee.work_email_taken", DUPLICATE_MESSAGE)
    );
    const user = userEvent.setup();
    renderDialog();

    await user.type(document.getElementById("new-emp-firstName")!, "Ayşe");
    await user.type(document.getElementById("new-emp-lastName")!, "Demir");
    await user.type(document.getElementById("new-emp-workEmail")!, "ayse@demo.local");
    await user.click(screen.getByRole("button", { name: trCommon.save }));

    const workEmail = document.getElementById("new-emp-workEmail")!;
    await waitFor(() => expect(workEmail.getAttribute("aria-invalid")).toBe("true"));
    expect(screen.getAllByText(DUPLICATE_MESSAGE).length).toBeGreaterThan(0);

    await user.type(workEmail, "x");
    await waitFor(() => expect(workEmail.getAttribute("aria-invalid")).not.toBe("true"));
  });
});
