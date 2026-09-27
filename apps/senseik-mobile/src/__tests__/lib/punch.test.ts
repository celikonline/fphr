import {
  accuracyLabel,
  availableActions,
  elapsedSinceCheckIn,
  formatClockDate,
  formatGmtOffset,
  nextPunchAction,
  punchTimes,
  shouldRenderMap,
} from "@/lib/punch";

const d = "2026-09-27";

describe("nextPunchAction", () => {
  it("walks the day in order", () => {
    expect(nextPunchAction({ isCheckedIn: false, date: d })).toBe("in");
    expect(nextPunchAction({ isCheckedIn: true, date: d, checkIn: "09:00:00" })).toBe("out");
    expect(
      nextPunchAction({
        isCheckedIn: true,
        date: d,
        checkIn: "09:00:00",
        onBreak: true,
        breakStartedAt: "12:00:00",
      })
    ).toBe("breakEnd");
    expect(
      nextPunchAction({ isCheckedIn: false, date: d, checkIn: "09:00:00", checkOut: "18:00:00" })
    ).toBe("done");
  });
});

describe("availableActions", () => {
  it("only allows a break while checked in", () => {
    expect(availableActions({ isCheckedIn: false, date: d }).breakStart).toBe(false);
    expect(availableActions({ isCheckedIn: true, date: d, checkIn: "09:00:00" })).toMatchObject({
      breakStart: true,
      breakEnd: false,
      out: true,
      in: false,
    });
    expect(
      availableActions({ isCheckedIn: true, date: d, checkIn: "09:00:00", onBreak: true })
    ).toMatchObject({ breakStart: false, breakEnd: true });
  });

  it("offers only Giriş before the first punch and nothing once the day is closed", () => {
    expect(availableActions({ isCheckedIn: false, date: d })).toEqual({
      in: true,
      out: false,
      breakStart: false,
      breakEnd: false,
    });
    expect(
      availableActions({ isCheckedIn: false, date: d, checkIn: "09:00:00", checkOut: "18:00:00" })
    ).toEqual({ in: false, out: false, breakStart: false, breakEnd: false });
  });

  it("still allows Çıkış during an open break (the server closes the break)", () => {
    expect(
      availableActions({ isCheckedIn: true, date: d, checkIn: "09:00:00", onBreak: true }).out
    ).toBe(true);
  });
});

describe("elapsedSinceCheckIn", () => {
  it("formats elapsed time", () =>
    expect(elapsedSinceCheckIn("10:00:00", new Date(2026, 8, 27, 12, 12))).toBe("2 sa 12 dk"));

  it("has nothing to show without a (valid) check-in", () => {
    expect(elapsedSinceCheckIn(null, new Date())).toBeNull();
    expect(elapsedSinceCheckIn(undefined, new Date())).toBeNull();
    expect(elapsedSinceCheckIn("bozuk", new Date())).toBeNull();
  });

  it("wraps past midnight for a night shift checked in yesterday evening", () =>
    expect(elapsedSinceCheckIn("22:30:00", new Date(2026, 8, 27, 1, 0))).toBe("2 sa 30 dk"));
});

describe("accuracyLabel", () => {
  it("labels accuracy", () => {
    expect(accuracyLabel(12)).toBe("Doğru");
    expect(accuracyLabel(50)).toBe("Doğru");
    expect(accuracyLabel(300)).toBe("Zayıf");
    expect(accuracyLabel(null)).toBeNull();
    expect(accuracyLabel(undefined)).toBeNull();
  });
});

describe("shouldRenderMap", () => {
  it("never renders a keyless Android map", () => {
    expect(shouldRenderMap({ hasMapsKey: false }, "android")).toBe(false);
    expect(shouldRenderMap(undefined, "android")).toBe(false);
    expect(shouldRenderMap({ hasMapsKey: true }, "android")).toBe(true);
    expect(shouldRenderMap(undefined, "ios")).toBe(true);
  });

  it("does not render a map on other platforms", () =>
    expect(shouldRenderMap({ hasMapsKey: true }, "web")).toBe(false));
});

describe("punchTimes", () => {
  it("reads Giriş/Çıkış and the open break start from the status as HH:mm", () => {
    expect(
      punchTimes(
        {
          isCheckedIn: true,
          date: d,
          checkIn: "09:05:12",
          onBreak: true,
          breakStartedAt: "12:30:00",
        },
        {}
      )
    ).toEqual({ in: "09:05", out: null, breakStart: "12:30", breakEnd: null });
  });

  it("falls back to this session's punches for a closed break", () => {
    expect(
      punchTimes(
        { isCheckedIn: true, date: d, checkIn: "09:00:00", breakMinutesToday: 15 },
        { breakStart: "12:00:00", breakEnd: "12:15:40" }
      )
    ).toEqual({ in: "09:00", out: null, breakStart: "12:00", breakEnd: "12:15" });
  });

  it("shows nothing before the status loads", () => {
    expect(punchTimes(null, {})).toEqual({
      in: null,
      out: null,
      breakStart: null,
      breakEnd: null,
    });
  });
});

describe("clock caption", () => {
  it("formats the date as '27 Eyl 2026'", () =>
    expect(formatClockDate(new Date(2026, 8, 27, 9, 0))).toBe("27 Eyl 2026"));

  it("formats the device's UTC offset", () => {
    const date = new Date(2026, 8, 27, 9, 0);
    const spy = jest.spyOn(date, "getTimezoneOffset");
    spy.mockReturnValue(-180);
    expect(formatGmtOffset(date)).toBe("GMT+3");
    spy.mockReturnValue(-330);
    expect(formatGmtOffset(date)).toBe("GMT+5:30");
    spy.mockReturnValue(0);
    expect(formatGmtOffset(date)).toBe("GMT");
    spy.mockReturnValue(240);
    expect(formatGmtOffset(date)).toBe("GMT-4");
  });
});
