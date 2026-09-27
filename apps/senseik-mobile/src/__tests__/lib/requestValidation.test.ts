import {
  isAdvanceRequestedDateAllowed,
  isOvertimeDurationValid,
  isPositiveAmount,
  isReceiptDateAllowed,
  parseAmountInput,
} from "@/lib/requestValidation";

describe("isPositiveAmount", () => {
  it("allows a positive amount", () => {
    expect(isPositiveAmount(0.01)).toBe(true);
  });

  it("rejects zero", () => {
    expect(isPositiveAmount(0)).toBe(false);
  });

  it("rejects a negative amount", () => {
    expect(isPositiveAmount(-5)).toBe(false);
  });
});

describe("isReceiptDateAllowed (ExpenseLimits.MaxReceiptDateDaysAhead = 1)", () => {
  const today = new Date("2026-09-27T12:00:00");

  it("allows today", () => {
    expect(isReceiptDateAllowed("2026-09-27", today)).toBe(true);
  });

  it("allows tomorrow (the 1-day slack)", () => {
    expect(isReceiptDateAllowed("2026-09-28", today)).toBe(true);
  });

  it("rejects the day after tomorrow", () => {
    expect(isReceiptDateAllowed("2026-09-29", today)).toBe(false);
  });

  it("allows a past date", () => {
    expect(isReceiptDateAllowed("2020-01-01", today)).toBe(true);
  });
});

describe("isAdvanceRequestedDateAllowed (ExpenseLimits.AdvanceDateMax{Past,Future}Days = 30/90)", () => {
  const today = new Date("2026-09-27T12:00:00");

  it("allows today", () => {
    expect(isAdvanceRequestedDateAllowed("2026-09-27", today)).toBe(true);
  });

  it("allows exactly 30 days in the past", () => {
    expect(isAdvanceRequestedDateAllowed("2026-08-28", today)).toBe(true);
  });

  it("rejects 31 days in the past", () => {
    expect(isAdvanceRequestedDateAllowed("2026-08-27", today)).toBe(false);
  });

  it("allows exactly 90 days in the future", () => {
    expect(isAdvanceRequestedDateAllowed("2026-12-26", today)).toBe(true);
  });

  it("rejects 91 days in the future", () => {
    expect(isAdvanceRequestedDateAllowed("2026-12-27", today)).toBe(false);
  });
});

describe("isOvertimeDurationValid (OvertimeLimits.MaxDurationMinutes = 1440)", () => {
  it("allows 1 minute", () => {
    expect(isOvertimeDurationValid(1)).toBe(true);
  });

  it("allows exactly 1440 minutes (24 hours)", () => {
    expect(isOvertimeDurationValid(1440)).toBe(true);
  });

  it("rejects 0", () => {
    expect(isOvertimeDurationValid(0)).toBe(false);
  });

  it("rejects 1441", () => {
    expect(isOvertimeDurationValid(1441)).toBe(false);
  });

  it("rejects a non-integer value", () => {
    expect(isOvertimeDurationValid(30.5)).toBe(false);
  });
});

describe("parseAmountInput", () => {
  it("parses a dot-decimal value", () => {
    expect(parseAmountInput("125.50")).toBe(125.5);
  });

  it("parses a Turkish comma-decimal value", () => {
    expect(parseAmountInput("125,50")).toBe(125.5);
  });

  it("parses a whole number", () => {
    expect(parseAmountInput("100")).toBe(100);
  });

  it("returns null for an empty string", () => {
    expect(parseAmountInput("")).toBeNull();
  });

  it("returns null for whitespace-only input", () => {
    expect(parseAmountInput("   ")).toBeNull();
  });

  it("returns null for non-numeric input", () => {
    expect(parseAmountInput("abc")).toBeNull();
  });
});
