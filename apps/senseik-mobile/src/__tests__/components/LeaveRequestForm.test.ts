import {
  buildCapturedPhotoFileName,
  buildPreviewInput,
  clampEndDate,
  formatHourlyDuration,
  hasRequiredDocument,
  leaveUnitFlags,
  previewSummaryText,
} from "@/components/leave/LeaveRequestForm";
import type { LeaveRequestPreview } from "@/services/LeaveService";

describe("clampEndDate (D-mobil-005)", () => {
  it("keeps the current end date when it is still on/after the new start date", () => {
    expect(clampEndDate("2026-11-10", "2026-11-15")).toBe("2026-11-15");
  });

  it("keeps the current end date when it equals the new start date", () => {
    expect(clampEndDate("2026-11-10", "2026-11-10")).toBe("2026-11-10");
  });

  it("pulls the end date up to the new (later) start date", () => {
    expect(clampEndDate("2026-11-20", "2026-11-15")).toBe("2026-11-20");
  });
});

describe("formatHourlyDuration (A: saatlik izin)", () => {
  it("formats a positive range as 'X sa Y dk'", () => {
    expect(formatHourlyDuration("09:00", "11:30")).toBe("2 sa 30 dk");
  });

  it("formats an exact-hour range without leftover minutes", () => {
    expect(formatHourlyDuration("09:00", "12:00")).toBe("3 sa 0 dk");
  });

  it("returns null when the end time is before the start time", () => {
    expect(formatHourlyDuration("14:00", "09:00")).toBeNull();
  });

  it("returns null when the end time equals the start time (zero duration)", () => {
    expect(formatHourlyDuration("09:00", "09:00")).toBeNull();
  });

  it("returns null for a malformed time string", () => {
    expect(formatHourlyDuration("9:00", "11:30")).toBeNull();
  });
});

describe("leaveUnitFlags", () => {
  it("treats an undefined unit as fullDay", () => {
    expect(leaveUnitFlags(undefined)).toEqual({
      isFullDay: true,
      isHalfDay: false,
      isHourly: false,
    });
  });

  it("flags fullDay", () => {
    expect(leaveUnitFlags("fullDay")).toEqual({
      isFullDay: true,
      isHalfDay: false,
      isHourly: false,
    });
  });

  it("flags halfDay", () => {
    expect(leaveUnitFlags("halfDay")).toEqual({
      isFullDay: false,
      isHalfDay: true,
      isHourly: false,
    });
  });

  it("flags hourly", () => {
    expect(leaveUnitFlags("hourly")).toEqual({
      isFullDay: false,
      isHalfDay: false,
      isHourly: true,
    });
  });
});

describe("hasRequiredDocument (B-mobil-004)", () => {
  it("allows a type that does not require a document, regardless of document count", () => {
    expect(hasRequiredDocument({ requiresDocument: false }, 0)).toBe(true);
  });

  it("blocks a requiresDocument type with no uploaded documents", () => {
    expect(hasRequiredDocument({ requiresDocument: true }, 0)).toBe(false);
  });

  it("allows a requiresDocument type once at least one document is uploaded", () => {
    expect(hasRequiredDocument({ requiresDocument: true }, 1)).toBe(true);
  });

  it("allows when no type is selected yet", () => {
    expect(hasRequiredDocument(null, 0)).toBe(true);
  });
});

describe("buildCapturedPhotoFileName (B10)", () => {
  it("builds '<leave type> <yyyy-MM-dd> <HH.mm>.jpg' for a JPEG capture", () => {
    const capturedAt = new Date(2026, 8, 28, 13, 24, 0);

    expect(buildCapturedPhotoFileName("Hastalık izni", capturedAt, "image/jpeg")).toBe(
      "Hastalık izni 2026-09-28 13.24.jpg"
    );
  });

  it("uses a .png extension for a PNG capture", () => {
    const capturedAt = new Date(2026, 8, 28, 13, 24, 0);

    expect(buildCapturedPhotoFileName("Hastalık izni", capturedAt, "image/png")).toBe(
      "Hastalık izni 2026-09-28 13.24.png"
    );
  });

  it("falls back to 'Belge' when the leave type name is unknown", () => {
    const capturedAt = new Date(2026, 8, 28, 13, 24, 0);

    expect(buildCapturedPhotoFileName(null, capturedAt, "image/jpeg")).toBe(
      "Belge 2026-09-28 13.24.jpg"
    );
    expect(buildCapturedPhotoFileName(undefined, capturedAt, "image/jpeg")).toBe(
      "Belge 2026-09-28 13.24.jpg"
    );
  });

  it("strips unsafe filename characters from the leave type name", () => {
    const capturedAt = new Date(2026, 8, 28, 13, 24, 0);

    expect(buildCapturedPhotoFileName("İzin/Tür:Adı", capturedAt, "image/jpeg")).toBe(
      "İzin Tür Adı 2026-09-28 13.24.jpg"
    );
  });

  it("zero-pads single-digit day/month/hour/minute", () => {
    const capturedAt = new Date(2026, 0, 5, 9, 5, 0);

    expect(buildCapturedPhotoFileName("Rapor", capturedAt, "image/jpeg")).toBe(
      "Rapor 2026-01-05 09.05.jpg"
    );
  });
});

describe("buildPreviewInput (B-mobil-006)", () => {
  it("returns null when no type is selected", () => {
    expect(
      buildPreviewInput(null, "2026-09-28", "2026-09-28", "09:00", "18:00", "morning")
    ).toBeNull();
  });

  it("returns null when the start date is empty", () => {
    expect(
      buildPreviewInput(
        { id: "type-1", unit: "fullDay" },
        "",
        "2026-09-28",
        "09:00",
        "18:00",
        "morning"
      )
    ).toBeNull();
  });

  it("returns null for a full-day type with no end date yet", () => {
    expect(
      buildPreviewInput(
        { id: "type-1", unit: "fullDay" },
        "2026-09-28",
        "",
        "09:00",
        "18:00",
        "morning"
      )
    ).toBeNull();
  });

  it("returns null for an hourly type with an invalid time range", () => {
    expect(
      buildPreviewInput(
        { id: "type-1", unit: "hourly" },
        "2026-09-28",
        "2026-09-28",
        "14:00",
        "09:00",
        "morning"
      )
    ).toBeNull();
  });

  it("builds the query for a full-day type", () => {
    expect(
      buildPreviewInput(
        { id: "type-1", unit: "fullDay" },
        "2026-09-28",
        "2026-09-30",
        "09:00",
        "18:00",
        "morning"
      )
    ).toEqual({ leaveTypeId: "type-1", start: "2026-09-28", end: "2026-09-30" });
  });

  it("builds the query for a half-day type with the same start/end date", () => {
    expect(
      buildPreviewInput(
        { id: "type-1", unit: "halfDay" },
        "2026-09-28",
        "2026-09-30",
        "09:00",
        "18:00",
        "afternoon"
      )
    ).toEqual({
      leaveTypeId: "type-1",
      start: "2026-09-28",
      end: "2026-09-28",
      halfDayPeriod: "afternoon",
    });
  });

  it("builds the query for an hourly type with startTime/endTime and the same start/end date", () => {
    expect(
      buildPreviewInput(
        { id: "type-1", unit: "hourly" },
        "2026-09-28",
        "2026-09-30",
        "09:00",
        "11:00",
        "morning"
      )
    ).toEqual({
      leaveTypeId: "type-1",
      start: "2026-09-28",
      end: "2026-09-28",
      startTime: "09:00:00",
      endTime: "11:00:00",
    });
  });
});

describe("previewSummaryText (B-mobil-006)", () => {
  function buildPreview(overrides: Partial<LeaveRequestPreview> = {}): LeaveRequestPreview {
    return { days: 1, weeklyRestDays: 0, publicHolidayDays: 0, warnings: [], ...overrides };
  }

  it("returns null when there is no preview yet", () => {
    expect(previewSummaryText(null)).toBeNull();
  });

  it("shows the day count when there is at least one day to deduct", () => {
    expect(previewSummaryText(buildPreview({ days: 2.5 }))).toBe("2,5 gün izninizden düşülecek");
  });

  it("shows the B12 message when the preview resolves to zero days (e.g. a Sunday-only request)", () => {
    expect(previewSummaryText(buildPreview({ days: 0 }))).toBe("Bu tarihlerde düşülecek gün yok");
  });
});
