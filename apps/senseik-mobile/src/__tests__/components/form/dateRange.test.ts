import {
  applyDayTap,
  daysInclusive,
  formatDayMonth,
  formatDayMonthYear,
  formatMonthYear,
  formatShortRange,
  isDayInRange,
  monthMatrix,
  shiftMonth,
} from "@/components/form/dateRange";

it("counts inclusive days", () => {
  expect(daysInclusive("2026-01-11", "2026-01-23")).toBe(13);
  expect(daysInclusive("2026-01-11", "2026-01-11")).toBe(1);
});

it("builds a Monday-first month grid", () => {
  const m = monthMatrix(2026, 8); // Eylül 2026, 1 Eylül Salı
  expect(m[0][0]).toBeNull();
  expect(m[0][1]).toBe("2026-09-01");
  expect(m.flat().filter(Boolean)).toHaveLength(30);
});

it("selects ranges and swaps reversed taps", () => {
  let s = applyDayTap({ mode: "range" }, "2026-01-23");
  s = applyDayTap({ mode: "range", ...s }, "2026-01-11");
  expect(s).toEqual({ from: "2026-01-11", to: "2026-01-23" });
  expect(
    applyDayTap({ mode: "range", from: "2026-01-11", to: "2026-01-23" }, "2026-02-01")
  ).toEqual({ from: "2026-02-01", to: undefined });
  expect(applyDayTap({ mode: "single" }, "2026-01-05")).toEqual({
    from: "2026-01-05",
    to: "2026-01-05",
  });
});

describe("daysInclusive edge cases", () => {
  it("counts across a month and year boundary", () => {
    expect(daysInclusive("2026-12-30", "2027-01-02")).toBe(4);
  });

  it("counts across the March DST switch without drifting (local dates, not ms/24h)", () => {
    expect(daysInclusive("2026-03-28", "2026-03-31")).toBe(4);
  });

  it("counts a leap day", () => {
    expect(daysInclusive("2028-02-28", "2028-03-01")).toBe(3);
  });
});

describe("monthMatrix", () => {
  it("always returns 6 rows of 7 cells", () => {
    const m = monthMatrix(2026, 1); // Şubat 2026 starts on a Sunday
    expect(m).toHaveLength(6);
    m.forEach((row) => expect(row).toHaveLength(7));
  });

  it("puts a Sunday-first month's 1st in the last column", () => {
    const m = monthMatrix(2026, 1); // 1 Şubat 2026 Pazar
    expect(m[0].slice(0, 6).every((cell) => cell === null)).toBe(true);
    expect(m[0][6]).toBe("2026-02-01");
    expect(m.flat().filter(Boolean)).toHaveLength(28);
  });

  it("puts a Monday-first month's 1st in the first column", () => {
    const m = monthMatrix(2026, 5); // 1 Haziran 2026 Pazartesi
    expect(m[0][0]).toBe("2026-06-01");
  });
});

describe("applyDayTap", () => {
  it("completes a range when the second tap is after the start", () => {
    expect(applyDayTap({ mode: "range", from: "2026-01-11" }, "2026-01-15")).toEqual({
      from: "2026-01-11",
      to: "2026-01-15",
    });
  });

  it("allows a one-day range by tapping the start again", () => {
    expect(applyDayTap({ mode: "range", from: "2026-01-11" }, "2026-01-11")).toEqual({
      from: "2026-01-11",
      to: "2026-01-11",
    });
  });

  it("single mode replaces any previous range", () => {
    expect(
      applyDayTap({ mode: "single", from: "2026-01-01", to: "2026-01-09" }, "2026-01-05")
    ).toEqual({ from: "2026-01-05", to: "2026-01-05" });
  });
});

describe("range helpers", () => {
  it("isDayInRange is inclusive on both ends and false without an end", () => {
    expect(isDayInRange("2026-01-11", "2026-01-11", "2026-01-23")).toBe(true);
    expect(isDayInRange("2026-01-23", "2026-01-11", "2026-01-23")).toBe(true);
    expect(isDayInRange("2026-01-24", "2026-01-11", "2026-01-23")).toBe(false);
    expect(isDayInRange("2026-01-12", "2026-01-11", undefined)).toBe(false);
  });

  it("shiftMonth wraps the year in both directions", () => {
    expect(shiftMonth(2026, 11, 1)).toEqual({ year: 2027, month0: 0 });
    expect(shiftMonth(2026, 0, -1)).toEqual({ year: 2025, month0: 11 });
  });
});

describe("Turkish date labels", () => {
  it("formats a month header", () => {
    expect(formatMonthYear(2026, 8)).toBe("Eylül 2026");
  });

  it("formats a short day-month", () => {
    expect(formatDayMonth("2026-09-29")).toBe("29 Eyl");
    expect(formatDayMonth("2026-10-03")).toBe("3 Eki");
  });

  it("formats a day-month-year", () => {
    expect(formatDayMonthYear("2026-01-05")).toBe("5 Oca 2026");
  });

  it("formats a short range with an arrow, collapsing a single day", () => {
    expect(formatShortRange("2026-09-29", "2026-10-03")).toBe("29 Eyl → 3 Eki");
    expect(formatShortRange("2026-09-29", "2026-09-29")).toBe("29 Eyl");
  });
});
