import { beforeAll, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { I18nextProvider } from "react-i18next";
import i18next from "i18next";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MantineProvider } from "@mantine/core";
import trWorkflow from "../../public/locales/tr/workflow.json";
import trCommon from "../../public/locales/tr/common.json";
import trLeave from "../../public/locales/tr/leave.json";
import WorkflowInboxPage from "./inbox";
import { decideTask, type InboxTaskDto } from "@/services/workflow.service";

// senseik-047 D-01: the inbox card must show request-type-specific detail (overtime date/duration/reason, leave
// date range/type/day count) from InboxTaskDto.summary, never the raw subjectId GUID.
vi.mock("@/components/permission-guard", async () => {
  const actual = await vi.importActual<typeof import("@/components/permission-guard")>("@/components/permission-guard");
  return { ...actual, usePermission: () => true };
});

const OVERTIME_SUBJECT_ID = "11111111-1111-1111-1111-111111111111";
const LEAVE_SUBJECT_ID = "22222222-2222-2222-2222-222222222222";

const FIXTURES: InboxTaskDto[] = [
  {
    taskId: "task-overtime-1",
    approvalRequestId: "ar-overtime-1",
    requestType: "Overtime",
    subjectId: OVERTIME_SUBJECT_ID,
    requesterName: "Ayşe Yılmaz",
    stepNumber: 1,
    isFallbackRoleTask: false,
    submittedAt: "2026-09-10T08:30:00Z",
    summary: {
      requestDate: "2026-09-09",
      overtime: { workDate: "2026-09-09", startTime: "18:00:00", durationMinutes: 120, reason: "Ay sonu kapanışı" },
    },
  },
  {
    taskId: "task-leave-1",
    approvalRequestId: "ar-leave-1",
    requestType: "Leave",
    subjectId: LEAVE_SUBJECT_ID,
    requesterName: "Mehmet Demir",
    stepNumber: 1,
    isFallbackRoleTask: false,
    submittedAt: "2026-09-08T10:00:00Z",
    summary: {
      requestDate: "2026-09-15",
      leave: { startDate: "2026-09-15", endDate: "2026-09-17", leaveTypeCode: "annual", leaveTypeName: null, days: 3 },
    },
  },
];

vi.mock("@/services/workflow.service", async () => {
  const actual = await vi.importActual<typeof import("@/services/workflow.service")>("@/services/workflow.service");
  return { ...actual, listInbox: vi.fn(async () => FIXTURES), decideTask: vi.fn(), overrideDecideTask: vi.fn() };
});

// jsdom has no matchMedia; the page's useMediaQuery(...) call and Mantine's own components need one to render.
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
  // The decide dialog's autosize Textarea needs ResizeObserver and document.fonts.
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
    defaultNS: "workflow",
    ns: ["workflow", "common", "leave"],
    resources: { tr: { workflow: trWorkflow, common: trCommon, leave: trLeave } },
    interpolation: { escapeValue: false },
    react: { useSuspense: false },
  });
  return instance;
}

function renderInboxPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  render(
    <I18nextProvider i18n={createTestI18n()}>
      <QueryClientProvider client={queryClient}>
        <MantineProvider>
          <WorkflowInboxPage />
        </MantineProvider>
      </QueryClientProvider>
    </I18nextProvider>,
  );
}

describe("WorkflowInboxPage", () => {
  it("renders the overtime task's date, duration and reason instead of the raw subjectId", async () => {
    renderInboxPage();

    expect(await screen.findByText("Ay sonu kapanışı")).toBeTruthy();
    expect(screen.getByText("2 sa")).toBeTruthy();
    expect(screen.queryByText(OVERTIME_SUBJECT_ID)).toBeNull();
  });

  it("renders the leave task's type and day count instead of the raw subjectId", async () => {
    renderInboxPage();

    expect(await screen.findByText("Yıllık izin")).toBeTruthy();
    expect(screen.getByText("3 gün")).toBeTruthy();
    expect(screen.queryByText(LEAVE_SUBJECT_ID)).toBeNull();
  });

  // Ürün kararı (denetim A10): ret gerekçesi zorunlu, onayda yorum isteğe bağlı.
  describe("decide dialog comment rule", () => {
    async function openDecideDialog() {
      const user = userEvent.setup();
      renderInboxPage();
      const [firstDecide] = await screen.findAllByRole("button", { name: "Karar ver" });
      await user.click(firstDecide!);
      return user;
    }

    it("blocks rejecting without a comment and shows the field error", async () => {
      vi.mocked(decideTask).mockClear();
      const user = await openDecideDialog();

      await user.click(await screen.findByRole("button", { name: "Reddet" }));

      expect(await screen.findByText(trWorkflow.inbox.rejectCommentRequired)).toBeTruthy();
      expect(decideTask).not.toHaveBeenCalled();
    });

    it("treats a whitespace-only comment as missing", async () => {
      vi.mocked(decideTask).mockClear();
      const user = await openDecideDialog();

      await user.type(await screen.findByRole("textbox"), "   ");
      await user.click(screen.getByRole("button", { name: "Reddet" }));

      expect(await screen.findByText(trWorkflow.inbox.rejectCommentRequired)).toBeTruthy();
      expect(decideTask).not.toHaveBeenCalled();
    });

    it("rejects with the trimmed comment", async () => {
      vi.mocked(decideTask).mockClear().mockResolvedValue(undefined as never);
      const user = await openDecideDialog();

      await user.type(await screen.findByRole("textbox"), " Bütçe uygun değil ");
      await user.click(screen.getByRole("button", { name: "Reddet" }));

      await waitFor(() => expect(decideTask).toHaveBeenCalledWith("task-overtime-1", { outcome: "rejected", comment: "Bütçe uygun değil" }));
    });

    it("approves without a comment", async () => {
      vi.mocked(decideTask).mockClear().mockResolvedValue(undefined as never);
      const user = await openDecideDialog();

      await user.click(await screen.findByRole("button", { name: "Onayla" }));

      await waitFor(() => expect(decideTask).toHaveBeenCalledWith("task-overtime-1", { outcome: "approved", comment: undefined }));
    });
  });
});
