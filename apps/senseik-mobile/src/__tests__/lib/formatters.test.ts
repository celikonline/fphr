import {
  dateOnlyToDate,
  dateToTimeOnly,
  formatDateWithWeekday,
  formatDays,
  formatDurationMinutes,
  formatRelativeTime,
  parseTimeOnly,
  timeOnlyToDate,
  toLocalDateOnly,
} from "@/lib/formatters";

describe("formatDays", () => {
  it("rounds to at most one decimal using the Turkish decimal comma", () => {
    expect(formatDays(41.333333333333336)).toBe("41,3");
  });

  it("shows no decimal at all for a whole number", () => {
    expect(formatDays(41)).toBe("41");
  });

  it("shows no decimal for a whole number that arrives as x.0", () => {
    expect(formatDays(5.0)).toBe("5");
  });

  it("handles zero", () => {
    expect(formatDays(0)).toBe("0");
  });

  it("rounds a value ending exactly on a half-tenth", () => {
    expect(formatDays(2.05)).toBe("2,1");
  });
});

describe("toLocalDateOnly", () => {
  const originalTZ = process.env.TZ;

  beforeAll(() => {
    // Deterministic regardless of the host machine's timezone: the bug this guards against only
    // reproduces in a positive UTC offset (Turkey is UTC+3).
    process.env.TZ = "Europe/Istanbul";
  });

  afterAll(() => {
    process.env.TZ = originalTZ;
  });

  it("keeps the local calendar day between 00:00 and 03:00 (UTC+3 rolls back a day in UTC)", () => {
    const localMidnightThirty = new Date(2026, 8, 16, 1, 30, 0);

    expect(toLocalDateOnly(localMidnightThirty)).toBe("2026-09-16");
    // Sanity check that this scenario would actually break the old `toISOString().slice(0, 10)`
    // approach - i.e. that the test exercises the UTC+3 rollback bug it claims to guard against.
    expect(localMidnightThirty.toISOString().slice(0, 10)).toBe("2026-09-15");
  });

  it("keeps the correct month at the first day of the month, early morning (month-start boundary)", () => {
    const localNewYearNight = new Date(2026, 0, 1, 1, 0, 0);

    expect(toLocalDateOnly(localNewYearNight)).toBe("2026-01-01");
    expect(localNewYearNight.toISOString().slice(0, 10)).toBe("2025-12-31");
  });

  it("formats a plain daytime date the same way both approaches would", () => {
    const noon = new Date(2026, 5, 10, 12, 0, 0);

    expect(toLocalDateOnly(noon)).toBe("2026-06-10");
  });
});

describe("dateOnlyToDate", () => {
  it("is the inverse of toLocalDateOnly", () => {
    expect(toLocalDateOnly(dateOnlyToDate("2026-11-16"))).toBe("2026-11-16");
  });

  it("falls back to the current date for a malformed value instead of throwing", () => {
    expect(() => dateOnlyToDate("not-a-date")).not.toThrow();
  });
});

describe("formatDateWithWeekday (D-mobil-005: DateField display)", () => {
  it("formats as 'gg Ay yyyy Hft' (day, short month, year, short weekday)", () => {
    // 2026-11-16 is a Monday ("Pzt" in Turkish).
    expect(formatDateWithWeekday("2026-11-16")).toBe("16 Kas 2026 Pzt");
  });
});

describe("formatDurationMinutes", () => {
  it("formats hours and leftover minutes", () => {
    expect(formatDurationMinutes(150)).toBe("2 sa 30 dk");
  });

  it("formats an exact number of hours with 0 dk", () => {
    expect(formatDurationMinutes(180)).toBe("3 sa 0 dk");
  });

  it("formats less than an hour as 0 sa", () => {
    expect(formatDurationMinutes(45)).toBe("0 sa 45 dk");
  });
});

describe("parseTimeOnly", () => {
  it("parses a valid HH:mm string", () => {
    expect(parseTimeOnly("09:30")).toEqual({ hours: 9, minutes: 30 });
  });

  it("returns null for an out-of-range hour", () => {
    expect(parseTimeOnly("24:00")).toBeNull();
  });

  it("returns null for an out-of-range minute", () => {
    expect(parseTimeOnly("09:60")).toBeNull();
  });

  it("returns null for a malformed string", () => {
    expect(parseTimeOnly("9:30")).toBeNull();
  });
});

describe("timeOnlyToDate / dateToTimeOnly", () => {
  it("round-trips a valid HH:mm string", () => {
    expect(dateToTimeOnly(timeOnlyToDate("14:05"))).toBe("14:05");
  });

  it("falls back to 09:00 for a malformed value", () => {
    expect(dateToTimeOnly(timeOnlyToDate("garbage"))).toBe("09:00");
  });
});

describe("formatRelativeTime (N1)", () => {
  const now = new Date("2026-09-27T12:00:00Z");

  it("shows 'az önce' for anything under a minute old", () => {
    expect(formatRelativeTime("2026-09-27T11:59:30Z", now)).toBe("az önce");
  });

  it("shows minutes for under an hour", () => {
    expect(formatRelativeTime("2026-09-27T11:55:00Z", now)).toBe("5 dk önce");
  });

  it("shows hours for under a day", () => {
    expect(formatRelativeTime("2026-09-27T10:00:00Z", now)).toBe("2 sa önce");
  });

  it("shows days for under a week", () => {
    expect(formatRelativeTime("2026-09-25T12:00:00Z", now)).toBe("2 gün önce");
  });

  it("falls back to an absolute date beyond a week", () => {
    expect(formatRelativeTime("2026-09-01T12:00:00Z", now)).toBe(
      new Date("2026-09-01T12:00:00Z").toLocaleDateString("tr-TR", {
        day: "2-digit",
        month: "short",
        year: "numeric",
      })
    );
  });

  it("returns the raw string for an unparseable value instead of throwing", () => {
    expect(formatRelativeTime("not-a-date", now)).toBe("not-a-date");
  });

  it("defaults `now` to the current time when omitted", () => {
    const justNow = new Date().toISOString();
    expect(formatRelativeTime(justNow)).toBe("az önce");
  });
});
