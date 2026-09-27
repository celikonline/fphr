import { canLeaveRequestHaveAttachments, canWithdrawLeaveRequest } from "@/screens/app/LeaveScreen";

describe("canWithdrawLeaveRequest (H1)", () => {
  it("allows withdrawing a pending request", () => {
    expect(canWithdrawLeaveRequest({ status: "pending" })).toBe(true);
  });

  it.each(["approved", "rejected", "withdrawn", "cancelled"] as const)(
    "blocks withdrawing a %s request",
    (status) => {
      expect(canWithdrawLeaveRequest({ status })).toBe(false);
    }
  );
});

describe("canLeaveRequestHaveAttachments (B21)", () => {
  const requestableTypes = [
    { id: "type-sick", requiresDocument: true },
    { id: "type-annual", requiresDocument: false },
  ];

  it("shows the attachments link for a requiresDocument type (e.g. hastalık izni)", () => {
    expect(canLeaveRequestHaveAttachments("type-sick", requestableTypes)).toBe(true);
  });

  it("hides the attachments link for a type that does not require a document (e.g. yıllık izin)", () => {
    expect(canLeaveRequestHaveAttachments("type-annual", requestableTypes)).toBe(false);
  });

  it("hides the attachments link when the type is not in the (active-only) requestable-types list at all", () => {
    expect(canLeaveRequestHaveAttachments("type-deactivated", requestableTypes)).toBe(false);
  });

  it("hides the attachments link when there are no requestable types loaded yet", () => {
    expect(canLeaveRequestHaveAttachments("type-sick", [])).toBe(false);
  });
});
