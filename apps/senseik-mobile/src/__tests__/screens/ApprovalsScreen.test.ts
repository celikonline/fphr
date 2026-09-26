import { formatTime, requestTypeLabel } from "@/screens/app/ApprovalsScreen";

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
