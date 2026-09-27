import {
  buildRejectDecision,
  formatAttachmentsSummary,
  formatTime,
  leaveSummaryTypeLabel,
  loadLeaveTaskAttachments,
  requestTypeLabel,
} from "@/screens/app/ApprovalsScreen";
import { leaveService } from "@/services/LeaveService";

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
