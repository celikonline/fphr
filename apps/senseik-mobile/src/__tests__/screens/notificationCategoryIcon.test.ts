import { notificationCategoryIcon } from "@/screens/app/NotificationsScreen";

describe("notificationCategoryIcon (mobil yeniden tasarım Task 6)", () => {
  it("maps a known templateKey prefix to its category icon/accent", () => {
    expect(notificationCategoryIcon("payroll.payslip_ready")).toEqual({
      icon: "wallet-outline",
      accent: "coral",
    });
    expect(notificationCategoryIcon("leave.added_on_behalf")).toEqual({
      icon: "sunny-outline",
      accent: "green",
    });
    expect(notificationCategoryIcon("approval.task_assigned")).toEqual({
      icon: "checkmark-done-outline",
      accent: "purple",
    });
  });

  it("falls back to a neutral bell icon for an unrecognized prefix", () => {
    expect(notificationCategoryIcon("something.new_key")).toEqual({
      icon: "notifications-outline",
      accent: "gray",
    });
  });

  it("falls back for a templateKey with no dot separator", () => {
    expect(notificationCategoryIcon("nokey")).toEqual({
      icon: "notifications-outline",
      accent: "gray",
    });
  });
});
