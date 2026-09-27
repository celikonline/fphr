import { canWithdrawLeaveRequest } from "@/screens/app/LeaveScreen";

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
