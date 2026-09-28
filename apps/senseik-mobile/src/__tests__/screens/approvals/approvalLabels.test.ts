import { approvalTypeKey, requestTypeLabel } from "@/screens/app/approvals/approvalLabels";

describe("approvalTypeKey (QA-103: one normalizer for both labels and the 'Tür' filter)", () => {
  it("maps every known request/entity key to its filter key, case-insensitively", () => {
    expect(approvalTypeKey("Leave")).toBe("leave");
    expect(approvalTypeKey("LeaveRequest")).toBe("leave");
    expect(approvalTypeKey("leaverequest")).toBe("leave");
    expect(approvalTypeKey("Overtime")).toBe("overtime");
    expect(approvalTypeKey("OvertimeRequest")).toBe("overtime");
    expect(approvalTypeKey("Expense")).toBe("expense");
    // The org-policy key is "Expense", but Expense's own Workflow request type is "ExpenseClaim".
    expect(approvalTypeKey("ExpenseClaim")).toBe("expense");
    expect(approvalTypeKey("expenseclaim")).toBe("expense");
    expect(approvalTypeKey("Advance")).toBe("advance");
    expect(approvalTypeKey("AdvanceRequest")).toBe("advance");
  });

  it("returns null for a request type with no filter mapping (only counted under 'Tümü')", () => {
    expect(approvalTypeKey("Requisition")).toBeNull();
    expect(approvalTypeKey("AssetAssignment")).toBeNull();
    expect(approvalTypeKey("SomethingNew")).toBeNull();
  });
});

describe("requestTypeLabel", () => {
  it("uses the same Turkish label the 'Tür' filter uses for the four filterable types", () => {
    expect(requestTypeLabel("Leave")).toBe("İzin");
    expect(requestTypeLabel("Overtime")).toBe("Fazla mesai");
    expect(requestTypeLabel("Expense")).toBe("Masraf");
    expect(requestTypeLabel("ExpenseClaim")).toBe("Masraf");
    expect(requestTypeLabel("Advance")).toBe("Avans");
    expect(requestTypeLabel("AdvanceRequest")).toBe("Avans");
  });

  it("labels the other known unit-assignable types without a filter mapping", () => {
    expect(requestTypeLabel("Requisition")).toBe("Kadro talebi");
    expect(requestTypeLabel("AssetAssignment")).toBe("Zimmet");
  });

  it("labels a punch-correction task 'Eksik giriş/çıkış' (FG-004)", () => {
    expect(requestTypeLabel("PunchCorrection")).toBe("Eksik giriş/çıkış");
  });

  it("falls back to 'Talep' for a truly unknown type, never the raw server key", () => {
    expect(requestTypeLabel("SomeFutureRequestType")).toBe("Talep");
  });
});
