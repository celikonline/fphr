import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
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
import { toast } from "@/hooks/use-toast";
import { createEmployee } from "@/services/employees.service";
import { inviteEmployee } from "@/services/invitations.service";
import { getPersonnelNumberAssignment } from "@/services/personnel-numbers.service";
vi.mock("@/services/personnel-numbers.service", async (importOriginal) => ({ ...await importOriginal<typeof import("@/services/personnel-numbers.service")>(), getPersonnelNumberAssignment: vi.fn() }));
beforeEach(() => { vi.mocked(getPersonnelNumberAssignment).mockReset().mockResolvedValue({ configured: false }); });

// N-2 (ik-surec-testi-bulgu-raporu-2026-09-20): a duplicate work e-mail must be reported on the "İş e-postası"
// field, not only in the toast that disappears after a few seconds.
vi.mock("@/services/employees.service", () => ({ createEmployee: vi.fn() }));
vi.mock("@/services/invitations.service", () => ({ inviteEmployee: vi.fn() }));
const permission = vi.hoisted(() => ({ canInvite: true }));
vi.mock("@/components/permission-guard", () => ({ usePermission: () => permission.canInvite }));
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));
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

function renderDialog(canInvite = true) {
  vi.clearAllMocks();
  permission.canInvite = canInvite;
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
  it("blocks hiring while assignment cannot be resolved and allows retry", async () => {
    vi.mocked(getPersonnelNumberAssignment).mockRejectedValue(new Error("offline"));
    renderDialog();
    expect(await screen.findByRole("button", { name: trEmployees.personnelNumbers.retry })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: trCommon.save })).toBeDisabled();
    expect(createEmployee).not.toHaveBeenCalled();
    vi.mocked(getPersonnelNumberAssignment).mockResolvedValue({ configured: false });
    await userEvent.click(screen.getByRole("button", { name: trEmployees.personnelNumbers.retry }));
    await waitFor(() => expect(screen.getByRole("button", { name: trCommon.save })).toBeEnabled());
  });
  it("internal assignment omits any manually supplied number", async () => {
    vi.mocked(getPersonnelNumberAssignment).mockResolvedValue({ configured: true, assignmentMode: "internal", nextNumber: "10000001" });
    vi.mocked(createEmployee).mockResolvedValue({ id: "emp-1" } as never);
    renderDialog(false);
    const user = userEvent.setup();
    await screen.findByText(/10000001/);
    expect(screen.queryByLabelText(trEmployees.personnelNumbers.number)).not.toBeInTheDocument();
    await user.type(document.getElementById("new-emp-firstName")!, "Ayşe");
    await user.type(document.getElementById("new-emp-lastName")!, "Demir");
    await user.click(screen.getByRole("button", { name: trCommon.save }));
    await waitFor(() => expect(createEmployee).toHaveBeenCalledWith(expect.objectContaining({ sicilNumarasi: undefined })));
  });
  it("external assignment validates the fixed range and preserves leading zeroes", async () => {
    vi.mocked(getPersonnelNumberAssignment).mockResolvedValue({ configured: true, assignmentMode: "external", startNumber: "00000001", endNumber: "00000999" });
    vi.mocked(createEmployee).mockResolvedValue({ id: "emp-1" } as never);
    renderDialog(false);
    const user = userEvent.setup();
    const sicil = await screen.findByLabelText(new RegExp(trEmployees.personnelNumbers.number));
    await user.type(document.getElementById("new-emp-firstName")!, "Ayşe");
    await user.type(document.getElementById("new-emp-lastName")!, "Demir");
    await user.type(sicil, "00001000");
    await user.click(screen.getByRole("button", { name: trCommon.save }));
    expect(createEmployee).not.toHaveBeenCalled();
    await user.clear(sicil); await user.type(sicil, "00000123");
    await user.click(screen.getByRole("button", { name: trCommon.save }));
    await waitFor(() => expect(createEmployee).toHaveBeenCalledWith(expect.objectContaining({ sicilNumarasi: "00000123" })));
  });
  it("exhausted internal range blocks saving", async () => {
    vi.mocked(getPersonnelNumberAssignment).mockResolvedValue({ configured: true, assignmentMode: "internal" });
    renderDialog();
    await screen.findByText(trEmployees.personnelNumbers.exhausted);
    expect(screen.getByRole("button", { name: trCommon.save })).toBeDisabled();
  });
  it("refreshes assignment after save-and-new and blocks an exhausted next hire", async () => {
    vi.mocked(getPersonnelNumberAssignment).mockResolvedValueOnce({ configured: true, assignmentMode: "internal", nextNumber: "99999999" }).mockResolvedValue({ configured: true, assignmentMode: "internal" });
    vi.mocked(createEmployee).mockResolvedValue({ id: "emp-last" } as never);
    renderDialog(false); const user = userEvent.setup();
    await screen.findByText(/99999999/);
    await user.type(document.getElementById("new-emp-firstName")!, "Ayşe");
    await user.type(document.getElementById("new-emp-lastName")!, "Demir");
    await user.click(screen.getByRole("button", { name: trCommon.saveAndNew }));
    await screen.findByText(trEmployees.personnelNumbers.exhausted);
    expect(getPersonnelNumberAssignment).toHaveBeenCalledTimes(2);
    expect(screen.getByRole("button", { name: trCommon.save })).toBeDisabled();
    expect(createEmployee).toHaveBeenCalledTimes(1);
  });
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

  async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>, buttonName: string) {
    await user.type(document.getElementById("new-emp-firstName")!, "Ayşe");
    await user.type(document.getElementById("new-emp-lastName")!, "Demir");
    await user.click(screen.getByRole("button", { name: buttonName }));
  }

  it("credentials_switch_off_skips_invite_call", async () => {
    vi.mocked(createEmployee).mockResolvedValue({ id: "emp-1" } as never);
    const user = userEvent.setup();
    renderDialog();

    const toggle = screen.getByRole("switch", { name: trEmployees.create.sendCredentials });
    expect((toggle as HTMLInputElement).checked).toBe(true);
    await user.click(toggle);
    await fillAndSubmit(user, trCommon.save);

    await waitFor(() => expect(createEmployee).toHaveBeenCalled());
    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(expect.objectContaining({ title: trEmployees.invite.outcome.notrequested })),
    );
    expect(inviteEmployee).not.toHaveBeenCalled();
  });

  it("sends sendCredentials true by default", async () => {
    vi.mocked(createEmployee).mockResolvedValue({ id: "emp-2" } as never);
    vi.mocked(inviteEmployee).mockResolvedValue({
      employeeId: "emp-2",
      outcome: "Sent",
      sentToEmail: "a@b.c",
      emailSource: "Work",
    });
    renderDialog();
    await fillAndSubmit(userEvent.setup(), trCommon.save);

    await waitFor(() => expect(inviteEmployee).toHaveBeenCalledWith("emp-2", { sendCredentials: true }));
  });

  it("save_and_new_resets_form_keeps_modal: keeps dialog open with empty fields", async () => {
    vi.mocked(createEmployee).mockResolvedValue({ id: "emp-3" } as never);
    vi.mocked(inviteEmployee).mockResolvedValue({
      employeeId: "emp-3",
      outcome: "Sent",
      sentToEmail: "a@b.c",
      emailSource: "Work",
    });
    renderDialog();
    await fillAndSubmit(userEvent.setup(), trCommon.saveAndNew);

    await waitFor(() => expect(createEmployee).toHaveBeenCalledTimes(1));
    await waitFor(() => expect((document.getElementById("new-emp-firstName") as HTMLInputElement).value).toBe(""));
    expect(screen.getByRole("dialog")).not.toBeNull();
  });

  it("no_invite_permission_hides_switch_and_skips_call", async () => {
    vi.mocked(createEmployee).mockResolvedValue({ id: "emp-4" } as never);
    renderDialog(false);

    expect(screen.queryByRole("switch")).toBeNull();
    await fillAndSubmit(userEvent.setup(), trCommon.save);

    await waitFor(() => expect(createEmployee).toHaveBeenCalledTimes(1));
    expect(inviteEmployee).not.toHaveBeenCalled();
  });

  it("invite_failure_warns_with_server_message_but_creation_succeeds", async () => {
    vi.mocked(createEmployee).mockResolvedValue({ id: "emp-5" } as never);
    vi.mocked(inviteEmployee).mockRejectedValue(conflict("x", "SMTP kapalı"));
    renderDialog();
    await fillAndSubmit(userEvent.setup(), trCommon.save);

    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.objectContaining({ variant: "destructive", description: expect.stringContaining("SMTP kapalı") }),
      ),
    );
    expect(toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "success" }));
  });

  it("save_and_new_button_sits_in_the_form_action_row", () => {
    renderDialog();
    const form = document.querySelector("form")!;
    expect(form.contains(screen.getByRole("button", { name: trCommon.saveAndNew }))).toBe(true);
  });
});
