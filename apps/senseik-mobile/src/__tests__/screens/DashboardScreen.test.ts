import { formatUnreadBadge } from "@/screens/app/DashboardScreen";

describe("formatUnreadBadge (N1)", () => {
  it("shows the exact count up to 9", () => {
    expect(formatUnreadBadge(0)).toBe("0");
    expect(formatUnreadBadge(1)).toBe("1");
    expect(formatUnreadBadge(9)).toBe("9");
  });

  it("caps the badge at '9+' beyond 9", () => {
    expect(formatUnreadBadge(10)).toBe("9+");
    expect(formatUnreadBadge(42)).toBe("9+");
  });
});
