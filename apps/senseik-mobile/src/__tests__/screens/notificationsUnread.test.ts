import {
  unreadCountAfterMarkingAllRead,
  unreadCountAfterMarkingRead,
} from "@/screens/app/notificationsUnread";

describe("unreadCountAfterMarkingRead (N1)", () => {
  it("decrements by one when the notification was unread", () => {
    expect(unreadCountAfterMarkingRead(3, false)).toBe(2);
  });

  it("never goes below zero", () => {
    expect(unreadCountAfterMarkingRead(0, false)).toBe(0);
  });

  it("leaves the count unchanged when the notification was already read", () => {
    expect(unreadCountAfterMarkingRead(3, true)).toBe(3);
  });
});

describe("unreadCountAfterMarkingAllRead (N1)", () => {
  it("always resets to zero", () => {
    expect(unreadCountAfterMarkingAllRead()).toBe(0);
  });
});
