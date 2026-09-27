import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trLeave from "../../../public/locales/tr/leave.json";
import trCommon from "../../../public/locales/tr/common.json";
import { LeaveRequestDetailSheet } from "./leave-request-detail";
import type { LeaveRequestDetail } from "@/services/leave.service";
import {
  getEmployeeLeaveRequestAttachmentDownloadUrl,
  getMyLeaveRequestAttachmentDownloadUrl,
} from "@/services/leave.service";
import { toast } from "@/hooks/use-toast";

// Denetim A5-belge: clicking an attached file's name resolves a presigned download URL (15 dk) from the endpoint
// matching the drawer's scope ("me" vs an employee id) and opens it in a new tab; errors surface via a toast.
vi.mock("@/services/leave.service", async (importOriginal) => {
  const original = await importOriginal<typeof import("@/services/leave.service")>();
  return {
    ...original,
    getMyLeaveRequestAttachmentDownloadUrl: vi.fn(),
    getEmployeeLeaveRequestAttachmentDownloadUrl: vi.fn(),
  };
});
vi.mock("@/hooks/use-toast", () => ({ toast: vi.fn() }));

const mockedMyUrl = vi.mocked(getMyLeaveRequestAttachmentDownloadUrl);
const mockedEmployeeUrl = vi.mocked(getEmployeeLeaveRequestAttachmentDownloadUrl);
const mockedToast = vi.mocked(toast);

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

const baseDetail: Omit<LeaveRequestDetail, "employeeName" | "attachments"> = {
  id: "request-1",
  employeeId: "employee-1",
  leaveTypeId: "type-1",
  leaveTypeCode: "sick",
  startDate: "2026-10-05",
  endDate: "2026-10-05",
  calculatedDays: 1,
  description: null,
  status: "approved",
  source: "selfService",
  revisionNo: 0,
  submittedAt: "2026-09-20T10:00:00Z",
  decidedAt: null,
};

function renderSheet(scope: string) {
  const load = vi.fn<(id: string) => Promise<LeaveRequestDetail>>().mockResolvedValue({
    ...baseDetail,
    employeeName: "Ayşe Demir",
    attachments: [
      { documentId: "doc-9", fileName: "rapor.pdf", attachedAt: "2026-09-20T10:05:00Z" },
    ],
  });
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <LeaveRequestDetailSheet
            requestId="request-1"
            scope={scope}
            load={load}
            typeName={() => "Hastalık izni"}
            onClose={vi.fn()}
          />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>
  );
}

describe("LeaveRequestDetailSheet attachments (denetim A5-belge)", () => {
  it("resolves the download URL from the /me endpoint and opens it when scope is 'me'", async () => {
    mockedMyUrl.mockResolvedValue({
      url: "https://files.example/rapor.pdf?sig=abc",
      expiresAt: "2026-09-20T10:20:00Z",
      fileName: "rapor.pdf",
      contentType: "application/pdf",
    });
    const openSpy = vi.spyOn(window, "open").mockReturnValue(null);
    const user = userEvent.setup();
    renderSheet("me");

    await user.click(await screen.findByRole("button", { name: "rapor.pdf" }));

    await waitFor(() => expect(mockedMyUrl).toHaveBeenCalledWith("request-1", "doc-9"));
    expect(mockedEmployeeUrl).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(openSpy).toHaveBeenCalledWith(
        "https://files.example/rapor.pdf?sig=abc",
        "_blank",
        "noopener,noreferrer"
      )
    );
  });

  it("resolves the download URL from the employee endpoint when scope is an employee id", async () => {
    mockedEmployeeUrl.mockResolvedValue({
      url: "https://files.example/rapor.pdf?sig=def",
      expiresAt: "2026-09-20T10:20:00Z",
      fileName: "rapor.pdf",
      contentType: "application/pdf",
    });
    const openSpy = vi.spyOn(window, "open").mockReturnValue(null);
    const user = userEvent.setup();
    renderSheet("employee-1");

    await user.click(await screen.findByRole("button", { name: "rapor.pdf" }));

    await waitFor(() =>
      expect(mockedEmployeeUrl).toHaveBeenCalledWith("employee-1", "request-1", "doc-9")
    );
    expect(mockedMyUrl).not.toHaveBeenCalled();
    await waitFor(() =>
      expect(openSpy).toHaveBeenCalledWith(
        "https://files.example/rapor.pdf?sig=def",
        "_blank",
        "noopener,noreferrer"
      )
    );
  });

  it("shows a toast and does not open a tab when the download URL request fails", async () => {
    mockedMyUrl.mockRejectedValue(new Error("boom"));
    const openSpy = vi.spyOn(window, "open").mockReturnValue(null);
    const user = userEvent.setup();
    renderSheet("me");

    await user.click(await screen.findByRole("button", { name: "rapor.pdf" }));

    await waitFor(() =>
      expect(mockedToast).toHaveBeenCalledWith(expect.objectContaining({ variant: "destructive" }))
    );
    expect(openSpy).not.toHaveBeenCalled();
  });
});
