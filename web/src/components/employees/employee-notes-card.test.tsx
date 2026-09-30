import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trEmployees from "../../../public/locales/tr/employees.json";
import trCommon from "../../../public/locales/tr/common.json";
import { EmployeeNotesCard } from "./employee-notes-card";
import {
  createEmployeeNote,
  updateEmployeeNote,
  uploadNoteAttachment,
  type EmployeeNote,
} from "@/services/employee-notes.service";

const permissions = vi.hoisted(() => ({ granted: new Set<string>() }));

vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>(
    "@/components/permission-guard"
  );
  return { ...actual, usePermission: (key: string) => permissions.granted.has(key) };
});

const NOTE = vi.hoisted(() => ({
  id: "n1",
  employeeId: "emp-1",
  title: "Performans görüşmesi",
  content: "Hedefler konuşuldu.",
  importance: "high",
  authorUserId: "u1",
  authorName: "Ayşe Yılmaz",
  createdAt: "2026-09-20T08:00:00Z",
  attachment: null,
} as EmployeeNote));

vi.mock("@/services/employee-notes.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/employee-notes.service")>(
    "@/services/employee-notes.service"
  );
  return {
    ...actual,
    listEmployeeNotes: vi.fn().mockResolvedValue({
      items: [NOTE],
      page: 1,
      pageSize: 100,
      totalCount: 1,
      totalPages: 1,
      hasNext: false,
      hasPrevious: false,
    }),
    createEmployeeNote: vi.fn().mockResolvedValue({ ...NOTE, id: "n2" }),
    updateEmployeeNote: vi.fn().mockResolvedValue({ ...NOTE, id: "n2" }),
    deleteEmployeeNote: vi.fn(),
    uploadNoteAttachment: vi.fn().mockResolvedValue({ ...NOTE, id: "n2" }),
    deleteNoteAttachment: vi.fn(),
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
    ns: ["employees", "common"],
    resources: { tr: { employees: trEmployees, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <I18nextProvider i18n={instance}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <EmployeeNotesCard employeeId="emp-1" />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("EmployeeNotesCard", () => {
  beforeEach(() => {
    permissions.granted = new Set(["employee.notes.view", "employee.notes.manage"]);
    vi.mocked(createEmployeeNote).mockClear();
    vi.mocked(uploadNoteAttachment).mockClear();
    vi.mocked(updateEmployeeNote).mockClear();
  });

  it("notes_hidden_without_permission", () => {
    permissions.granted = new Set(["employee.my.view"]);
    renderCard();
    expect(screen.queryByText("Çalışan notları")).toBeNull();
    expect(screen.queryByText("Performans görüşmesi")).toBeNull();
  });

  it("lists notes for viewers and hides manage actions without manage permission", async () => {
    permissions.granted = new Set(["employee.notes.view"]);
    renderCard();
    expect(await screen.findByText("Performans görüşmesi")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Not ekle" })).toBeNull();
  });

  it("note_attachment_upload", async () => {
    const user = userEvent.setup();
    renderCard();
    await screen.findByText("Performans görüşmesi");
    await user.click(screen.getByRole("button", { name: "Not ekle" }));
    await user.type(await screen.findByLabelText(/^Başlık/), "Yeni not");
    const file = new File(["x"], "rapor.pdf", { type: "application/pdf" });
    await user.upload(screen.getByLabelText("Dosya ekle"), file);
    await user.click(screen.getByRole("button", { name: "Kaydet" }));
    await waitFor(() =>
      expect(createEmployeeNote).toHaveBeenCalledWith("emp-1", {
        title: "Yeni not",
        content: "",
        importance: "normal",
      })
    );
    await waitFor(() => expect(uploadNoteAttachment).toHaveBeenCalledWith("n2", file));
  });

  it("retry after a failed attachment updates the created note instead of creating another", async () => {
    vi.mocked(uploadNoteAttachment).mockRejectedValueOnce(new Error("boom"));
    const user = userEvent.setup();
    renderCard();
    await screen.findByText("Performans görüşmesi");
    await user.click(screen.getByRole("button", { name: "Not ekle" }));
    await user.type(await screen.findByLabelText(/^Başlık/), "Yeni not");
    await user.upload(
      screen.getByLabelText("Dosya ekle"),
      new File(["x"], "rapor.pdf", { type: "application/pdf" })
    );
    await user.click(screen.getByRole("button", { name: "Kaydet" }));
    await waitFor(() => expect(uploadNoteAttachment).toHaveBeenCalledTimes(1));
    await user.click(await screen.findByRole("button", { name: "Kaydet" }));
    await waitFor(() => expect(updateEmployeeNote).toHaveBeenCalledWith("n2", expect.anything()));
    expect(createEmployeeNote).toHaveBeenCalledTimes(1);
  });

  it("rejects an oversized or disallowed attachment before upload", async () => {
    const user = userEvent.setup({ applyAccept: false });
    renderCard();
    await screen.findByText("Performans görüşmesi");
    await user.click(screen.getByRole("button", { name: "Not ekle" }));
    const input = await screen.findByLabelText("Dosya ekle");
    const big = new File(["x"], "büyük.pdf", { type: "application/pdf" });
    Object.defineProperty(big, "size", { value: 11 * 1024 * 1024 });
    await user.upload(input, big);
    expect(await screen.findByText(/10 MB/)).toBeTruthy();
    await user.upload(input, new File(["x"], "virus.exe", { type: "application/x-msdownload" }));
    expect(await screen.findByText(/Yalnız PDF/)).toBeTruthy();
  });
});
