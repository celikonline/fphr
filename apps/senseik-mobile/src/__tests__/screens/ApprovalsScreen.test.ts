import {
  approvalFilterOptions,
  approvalFilterSelectedText,
  attachmentsCountLabel,
  buildRejectDecision,
  countByFilter,
  filterTasks,
  formatAttachmentsSummary,
  formatTime,
  leaveSummaryTypeLabel,
  loadLeaveTaskAttachments,
  requestTypeLabel,
} from "@/screens/app/ApprovalsScreen";
import type { InboxTask } from "@/services/ApprovalsService";
import { leaveService } from "@/services/LeaveService";

function buildTask(
  overrides: Partial<InboxTask> & Pick<InboxTask, "taskId" | "requestType">
): InboxTask {
  return {
    approvalRequestId: `${overrides.taskId}-request`,
    subjectId: `${overrides.taskId}-subject`,
    requesterName: "Ayşe Yılmaz",
    stepNumber: 1,
    isFallbackRoleTask: false,
    ...overrides,
  };
}

jest.mock("@/services/LeaveService");

describe("requestTypeLabel", () => {
  it("maps known request types to their Turkish label", () => {
    expect(requestTypeLabel("Leave")).toBe("İzin");
    expect(requestTypeLabel("Overtime")).toBe("Fazla mesai");
  });

  it("maps Requisition to Kadro talebi (D5)", () => {
    expect(requestTypeLabel("Requisition")).toBe("Kadro talebi");
  });

  it("maps AssetAssignment to Zimmet (D5)", () => {
    expect(requestTypeLabel("AssetAssignment")).toBe("Zimmet");
  });

  it("falls back to the raw request type when unknown", () => {
    expect(requestTypeLabel("SomethingNew")).toBe("SomethingNew");
  });
});

describe("formatTime", () => {
  it("trims a TimeOnly (HH:mm:ss) string to HH:mm", () => {
    expect(formatTime("08:30:00")).toBe("08:30");
  });
});

describe("leaveSummaryTypeLabel (B5)", () => {
  it("translates a raw seeded resource key leaveTypeName", () => {
    expect(
      leaveSummaryTypeLabel({ leaveTypeName: "leave.type.annual", leaveTypeCode: "annual" })
    ).toBe("Yıllık izin");
  });

  it("passes a free-form (custom type) leaveTypeName through unchanged", () => {
    expect(leaveSummaryTypeLabel({ leaveTypeName: "Özel türüm", leaveTypeCode: "custom" })).toBe(
      "Özel türüm"
    );
  });

  it("falls back to a humanized leaveTypeCode when leaveTypeName is missing", () => {
    expect(leaveSummaryTypeLabel({ leaveTypeName: null, leaveTypeCode: "annual" })).toBe("Annual");
  });

  it("falls back to a humanized leaveTypeCode when leaveTypeName is an empty string", () => {
    expect(leaveSummaryTypeLabel({ leaveTypeName: "", leaveTypeCode: "jobSearch" })).toBe(
      "Job Search"
    );
  });
});

describe("buildRejectDecision (B6: reject comment is now optional)", () => {
  it("omits the comment when the reason is empty", () => {
    expect(buildRejectDecision("")).toEqual({ outcome: "rejected" });
  });

  it("omits the comment when the reason is only whitespace", () => {
    expect(buildRejectDecision("   ")).toEqual({ outcome: "rejected" });
  });

  it("includes the trimmed comment when a reason is given", () => {
    expect(buildRejectDecision("  Bütçe uygun değil  ")).toEqual({
      outcome: "rejected",
      comment: "Bütçe uygun değil",
    });
  });
});

describe("formatAttachmentsSummary (A1)", () => {
  it("returns null when there are no attachments", () => {
    expect(formatAttachmentsSummary([])).toBeNull();
  });

  it("formats a single attachment", () => {
    expect(formatAttachmentsSummary([{ fileName: "rapor.pdf" }])).toBe("📎 1 belge: rapor.pdf");
  });

  it("formats multiple attachments, comma-separated", () => {
    expect(
      formatAttachmentsSummary([
        { fileName: "rapor.pdf" },
        { fileName: "Hastalık izni 2026-09-27 01.55.jpg" },
      ])
    ).toBe("📎 2 belge: rapor.pdf, Hastalık izni 2026-09-27 01.55.jpg");
  });
});

describe("attachmentsCountLabel (senseik-mobile-011)", () => {
  it("formats the header above the tappable attachment rows", () => {
    expect(attachmentsCountLabel(1)).toBe("📎 1 belge:");
    expect(attachmentsCountLabel(3)).toBe("📎 3 belge:");
  });
});

describe("countByFilter / filterTasks (görev 8: Onaylarım tür filtresi)", () => {
  const tasks: InboxTask[] = [
    buildTask({ taskId: "task-leave", requestType: "Leave" }),
    buildTask({ taskId: "task-overtime", requestType: "Overtime" }),
    buildTask({ taskId: "task-expense", requestType: "Expense" }),
  ];

  it("counts every task under 'all' and each task once under its own type", () => {
    expect(countByFilter(tasks)).toEqual({
      all: 3,
      leave: 1,
      overtime: 1,
      expense: 1,
      advance: 0,
    });
  });

  it("counts a request type with no filter mapping (e.g. Requisition) only under 'all'", () => {
    const withUnknownType = [
      ...tasks,
      buildTask({ taskId: "task-req", requestType: "Requisition" }),
    ];

    expect(countByFilter(withUnknownType)).toEqual({
      all: 4,
      leave: 1,
      overtime: 1,
      expense: 1,
      advance: 0,
    });
  });

  it("filters to only the matching request type", () => {
    expect(filterTasks(tasks, "overtime")).toEqual([tasks[1]]);
  });

  it("returns every task for the 'all' filter", () => {
    expect(filterTasks(tasks, "all")).toEqual(tasks);
  });

  it("builds the 'Tür' sheet options in Tümü/İzin/Fazla mesai/Masraf/Avans order, with counts", () => {
    const counts = countByFilter(tasks);

    expect(approvalFilterOptions(counts)).toEqual([
      { value: "all", label: "Tümü (3)" },
      { value: "leave", label: "İzin (1)" },
      { value: "overtime", label: "Fazla mesai (1)" },
      { value: "expense", label: "Masraf (1)" },
      { value: "advance", label: "Avans (0)" },
    ]);
  });

  it("formats the selected value shown on the 'Tür' row", () => {
    const counts = countByFilter(tasks);

    expect(approvalFilterSelectedText("overtime", counts)).toBe("Fazla mesai (1)");
    expect(approvalFilterSelectedText("all", counts)).toBe("Tümü (3)");
  });
});

describe("loadLeaveTaskAttachments (A1)", () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it("calls the manager-scoped detail endpoint with the requester's employeeId and the request id", async () => {
    (leaveService.getEmployeeLeaveRequest as jest.Mock).mockResolvedValue({
      attachments: [
        { documentId: "doc-1", fileName: "rapor.pdf", attachedAt: "2026-09-27T01:55:00Z" },
      ],
    });

    const result = await loadLeaveTaskAttachments({
      requestType: "Leave",
      requesterEmployeeId: "employee-1",
      subjectId: "request-1",
    });

    expect(leaveService.getEmployeeLeaveRequest).toHaveBeenCalledWith("employee-1", "request-1");
    expect(result).toEqual([
      { documentId: "doc-1", fileName: "rapor.pdf", attachedAt: "2026-09-27T01:55:00Z" },
    ]);
  });

  it("returns an empty array when the request has no attachments", async () => {
    (leaveService.getEmployeeLeaveRequest as jest.Mock).mockResolvedValue({ attachments: null });

    const result = await loadLeaveTaskAttachments({
      requestType: "Leave",
      requesterEmployeeId: "employee-1",
      subjectId: "request-1",
    });

    expect(result).toEqual([]);
  });

  it("skips the request entirely for a non-Leave task", async () => {
    const result = await loadLeaveTaskAttachments({
      requestType: "Overtime",
      requesterEmployeeId: "employee-1",
      subjectId: "request-1",
    });

    expect(leaveService.getEmployeeLeaveRequest).not.toHaveBeenCalled();
    expect(result).toEqual([]);
  });

  it("skips the request when requesterEmployeeId is missing (older API build)", async () => {
    const result = await loadLeaveTaskAttachments({
      requestType: "Leave",
      requesterEmployeeId: null,
      subjectId: "request-1",
    });

    expect(leaveService.getEmployeeLeaveRequest).not.toHaveBeenCalled();
    expect(result).toEqual([]);
  });

  it("hides the row (resolves to []) instead of throwing on a 403/404/network error", async () => {
    (leaveService.getEmployeeLeaveRequest as jest.Mock).mockRejectedValue({
      isAxiosError: true,
      response: { status: 403 },
    });

    await expect(
      loadLeaveTaskAttachments({
        requestType: "Leave",
        requesterEmployeeId: "employee-1",
        subjectId: "request-1",
      })
    ).resolves.toEqual([]);
  });
});
