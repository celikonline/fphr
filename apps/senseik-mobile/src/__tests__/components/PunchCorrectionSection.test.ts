import {
  canSubmitPunchCorrection,
  punchCorrectionDefaults,
} from "@/components/requests/PunchCorrectionSection";

describe("punchCorrectionDefaults (FG-004: gün ve planlanan saat önceden dolu)", () => {
  it("defaults both times and checks both boxes when there is no record for the day", () => {
    expect(punchCorrectionDefaults(undefined)).toEqual({
      includeCheckIn: true,
      includeCheckOut: true,
      checkInTime: "09:00",
      checkOutTime: "18:00",
    });
  });

  it("prefers the planned shift time over the day's own (missing) punch", () => {
    const defaults = punchCorrectionDefaults({
      checkIn: null,
      checkOut: null,
      plannedStart: "08:30:00",
      plannedEnd: "17:30:00",
    });
    expect(defaults.checkInTime).toBe("08:30");
    expect(defaults.checkOutTime).toBe("17:30");
  });

  it("only checks the missing side, leaving a recorded punch unchecked", () => {
    const defaults = punchCorrectionDefaults({
      checkIn: "09:02:00",
      checkOut: null,
      plannedStart: "09:00:00",
      plannedEnd: "18:00:00",
    });
    expect(defaults.includeCheckIn).toBe(false);
    expect(defaults.includeCheckOut).toBe(true);
    expect(defaults.checkOutTime).toBe("18:00");
  });

  it("falls back to the day's own punch when there is no planned shift", () => {
    const defaults = punchCorrectionDefaults({
      checkIn: "09:05:00",
      checkOut: null,
      plannedStart: null,
      plannedEnd: null,
    });
    expect(defaults.checkInTime).toBe("09:05");
  });
});

describe("canSubmitPunchCorrection (FG-004)", () => {
  const today = new Date("2026-09-27T12:00:00");
  const validParams = {
    date: "2026-09-27",
    includeCheckIn: true,
    includeCheckOut: false,
    checkInTime: "09:00",
    checkOutTime: "18:00",
    reason: "Turnike arızalıydı",
    today,
  };

  it("allows a valid single-time submission", () => {
    expect(canSubmitPunchCorrection(validParams)).toBe(true);
  });

  it("allows both times when the check-out is after the check-in", () => {
    expect(
      canSubmitPunchCorrection({ ...validParams, includeCheckIn: true, includeCheckOut: true })
    ).toBe(true);
  });

  it("blocks a future date", () => {
    expect(canSubmitPunchCorrection({ ...validParams, date: "2026-09-28" })).toBe(false);
  });

  it("blocks neither time being included", () => {
    expect(
      canSubmitPunchCorrection({ ...validParams, includeCheckIn: false, includeCheckOut: false })
    ).toBe(false);
  });

  it("blocks a check-out at or before the check-in once both are given", () => {
    expect(
      canSubmitPunchCorrection({
        ...validParams,
        includeCheckIn: true,
        includeCheckOut: true,
        checkInTime: "18:00",
        checkOutTime: "09:00",
      })
    ).toBe(false);
  });

  it("blocks an empty or whitespace-only reason", () => {
    expect(canSubmitPunchCorrection({ ...validParams, reason: "" })).toBe(false);
    expect(canSubmitPunchCorrection({ ...validParams, reason: "   " })).toBe(false);
  });
});
