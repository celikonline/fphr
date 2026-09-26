import { formatDays, toLocalDateOnly } from "@/lib/formatters";

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
