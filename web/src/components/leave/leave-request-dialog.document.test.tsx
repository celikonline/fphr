import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trLeave from "../../../public/locales/tr/leave.json";
import trCommon from "../../../public/locales/tr/common.json";
import { LeaveRequestDialog, type LeaveRequestTypeOption } from "./leave-request-dialog";
import type { LeaveRequest, LeaveRequestDetail, LeaveRequestInput } from "@/services/leave.service";
import { uploadDocumentFile } from "@/services/documents.service";

// Denetim raporu A5-belge: a type with `requiresDocument` shows an upload section, blocks the submit until a
// document is staged, and sends `documentIds`; editing a pending request lists the already-linked files.
vi.mock("@/services/documents.service", () => ({
  uploadDocumentFile: vi.fn(),
}));
vi.mock("@/services/leave.service", async (importOriginal) => {
  const original = await importOriginal<typeof import("@/services/leave.service")>();
  return { ...original, attachMyLeaveDocument: vi.fn().mockResolvedValue(undefined), detachMyLeaveDocument: vi.fn().mockResolvedValue(undefined) };
});
vi.mock("@/components/ui/date-picker", () => ({
  DatePicker: ({ id, value, onChange }: { id: string; value: string; onChange: (v: string) => void }) => (
    <input aria-label={id} value={value} onChange={(event) => onChange(event.target.value)} />
  ),
}));

const mockedUpload = vi.mocked(uploadDocumentFile);

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

function createTestI18n() {
  const instance = i18next.createInstance();
  void instance.init({
    lng: "tr",
    fallbackLng: "tr",
    defaultNS: "leave",
    ns: ["leave", "common"],
    resources: { tr: { leave: trLeave, common: trCommon } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function typeOption(overrides: Partial<LeaveRequestTypeOption> = {}): LeaveRequestTypeOption {
  return {
    id: "type-1",
    name: "Hastalık izni",
    description: null,
    requiresDescription: false,
    perRequestMaxDays: null,
    availableBalance: null,
    unit: "fullDay",
    ...overrides,
  };
}

const editingRequest: LeaveRequest = {
  id: "request-1",
  employeeId: "employee-1",
  leaveTypeId: "type-1",
  leaveTypeCode: "sick",
  startDate: "2026-10-05",
  endDate: "2026-10-05",
  calculatedDays: 1,
  description: null,
  status: "pending",
  source: "selfService",
  approvalRequestId: null,
  revisionNo: 0,
  submittedAt: "2026-09-20T10:00:00Z",
  decidedAt: null,
};

function renderDialog(type: LeaveRequestTypeOption, editing: LeaveRequest | null = null) {
  const save = vi.fn<(input: LeaveRequestInput, bypassApproval: boolean) => Promise<unknown>>().mockResolvedValue(undefined);
  const loadDetail = vi.fn<(requestId: string) => Promise<LeaveRequestDetail>>().mockResolvedValue({
    ...editing!,
    employeeName: "Ayşe Demir",
    attachments: [{ documentId: "doc-9", fileName: "rapor.pdf", attachedAt: "2026-09-20T10:05:00Z" }],
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <LeaveRequestDialog
            editing={editing}
            types={[type]}
            title="İzin talebi"
            description="Açıklama"
            submitLabel="Gönder"
            previewScope="me"
            preview={() => Promise.resolve({ days: 1, weeklyRestDays: 0, publicHolidayDays: 0, warnings: [] })}
            save={save}
            requesterEmployeeId="employee-1"
            loadDetail={loadDetail}
            documentUploadScope="me"
            onClose={vi.fn()}
            onSaved={vi.fn()}
          />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
  return { save, loadDetail };
}

async function fillDates(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("leave-start"), "2026-10-05");
  await user.type(screen.getByLabelText("leave-end"), "2026-10-05");
}

describe("LeaveRequestDialog document (denetim A5-belge)", () => {
  it("shows the upload section only for a type that requires a document", () => {
    renderDialog(typeOption({ requiresDocument: true }));

    expect(screen.getByLabelText("Belge")).toBeTruthy();
  });

  it("hides the upload section for an ordinary type", () => {
    renderDialog(typeOption());

    expect(screen.queryByLabelText("Belge")).toBeNull();
  });

  it("blocks the submit until a document is uploaded", async () => {
    const user = userEvent.setup();
    const { save } = renderDialog(typeOption({ requiresDocument: true }));
    await fillDates(user);

    await user.click(screen.getByRole("button", { name: "Gönder" }));

    expect((await screen.findByRole("alert")).textContent).toBe("Bu izin türünde belge eklemek zorunludur.");
    expect(save).not.toHaveBeenCalled();
  });

  it("sends the uploaded document's id", async () => {
    mockedUpload.mockResolvedValue("doc-1");
    const user = userEvent.setup();
    const { save } = renderDialog(typeOption({ requiresDocument: true }));
    await fillDates(user);

    await user.upload(screen.getByLabelText("Belge"), new File(["bytes"], "rapor.pdf", { type: "application/pdf" }));
    await waitFor(() => expect(screen.getByText("rapor.pdf")).toBeTruthy());
    expect(mockedUpload).toHaveBeenCalledWith(expect.any(File), "documents", "me");

    await user.click(screen.getByRole("button", { name: "Gönder" }));

    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0][0]).toMatchObject({ leaveTypeId: "type-1", documentIds: ["doc-1"] });
  });

  it("sends no document ids for an ordinary type", async () => {
    const user = userEvent.setup();
    const { save } = renderDialog(typeOption());
    await fillDates(user);

    await user.click(screen.getByRole("button", { name: "Gönder" }));

    await waitFor(() => expect(save).toHaveBeenCalledTimes(1));
    expect(save.mock.calls[0][0].documentIds).toBeUndefined();
  });

  it("lists the already-linked files when editing", async () => {
    renderDialog(typeOption({ requiresDocument: true }), editingRequest);

    await waitFor(() => expect(screen.getByText("rapor.pdf")).toBeTruthy());
  });
});
