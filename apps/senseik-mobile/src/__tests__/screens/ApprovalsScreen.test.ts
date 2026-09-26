import {
  buildRejectDecision,
  formatTime,
  leaveSummaryTypeLabel,
  requestTypeLabel,
} from "@/screens/app/ApprovalsScreen";

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
