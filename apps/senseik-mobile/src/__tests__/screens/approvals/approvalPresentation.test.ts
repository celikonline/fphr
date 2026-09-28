import {
  approvalAmountText,
  approvalCardHeading,
  approvalDetailRows,
  approvalTypeCaption,
} from "@/screens/app/approvals/approvalPresentation";
import type { InboxTask } from "@/services/ApprovalsService";

describe("approvalAmountText (görev 8)", () => {
  it("formats a leave task's day count", () => {
    expect(
      approvalAmountText({
        summary: {
          requestDate: "2026-09-20",
          leave: {
            startDate: "2026-10-01",
            endDate: "2026-10-05",
            leaveTypeCode: "annual",
            days: 5,
          },
        },
      })
    ).toBe("5 gün");
  });

  it("formats an overtime task's duration", () => {
    expect(
      approvalAmountText({
        summary: {
          requestDate: "2026-09-20",
          overtime: { workDate: "2026-09-20", startTime: "18:00:00", durationMinutes: 150 },
        },
      })
    ).toBe("2 sa 30 dk");
  });

  it("returns null when the task has no summary yet (Expense/Advance/...)", () => {
    expect(approvalAmountText({ summary: null })).toBeNull();
    expect(approvalAmountText({})).toBeNull();
  });

  it("formats a punch-correction task's requested times (FG-004)", () => {
    expect(
      approvalAmountText({
        summary: {
          requestDate: "2026-09-20",
          punchCorrection: {
            date: "2026-09-18",
            requestedCheckIn: "08:55:00",
            requestedCheckOut: "18:00:00",
            reason: "Turnike arızalıydı",
          },
        },
      })
    ).toBe("08:55 – 18:00");
  });

  it("shows an em-dash for the side that wasn't asked for", () => {
    expect(
      approvalAmountText({
        summary: {
          requestDate: "2026-09-20",
          punchCorrection: {
            date: "2026-09-18",
            requestedCheckOut: "18:00:00",
            reason: "Turnike arızalıydı",
          },
        },
      })
    ).toBe("— – 18:00");
  });
});

describe("approvalTypeCaption (görev 8)", () => {
  it("uses the leave type name when a leave summary is present", () => {
    expect(
      approvalTypeCaption({
        requestType: "Leave",
        summary: {
          requestDate: "2026-09-20",
          leave: {
            startDate: "2026-10-01",
            endDate: "2026-10-05",
            leaveTypeCode: "annual",
            leaveTypeName: "leave.type.annual",
            days: 5,
          },
        },
      })
    ).toBe("Yıllık izin");
  });

  it("falls back to the general request type label otherwise", () => {
    expect(approvalTypeCaption({ requestType: "Overtime", summary: null })).toBe("Fazla mesai");
    // QA-103: the label is "Masraf" (matching the "Tür" filter), whether the server sends the
    // org-policy key "Expense" or the request's own Workflow key "ExpenseClaim".
    expect(approvalTypeCaption({ requestType: "Expense", summary: null })).toBe("Masraf");
    expect(approvalTypeCaption({ requestType: "ExpenseClaim", summary: null })).toBe("Masraf");
  });
});

describe("approvalCardHeading (QA-103: never repeat the same text as heading and subtitle)", () => {
  it("uses the amount as heading and the type label as subtitle when a summary is present", () => {
    expect(
      approvalCardHeading({
        requestType: "Leave",
        summary: {
          requestDate: "2026-09-20",
          leave: {
            startDate: "2026-10-01",
            endDate: "2026-10-05",
            leaveTypeCode: "annual",
            leaveTypeName: "leave.type.annual",
            days: 5,
          },
        },
      })
    ).toEqual({ heading: "5 gün", subtitle: "Yıllık izin" });
  });

  it("uses the overtime duration as heading and 'Fazla mesai' as subtitle", () => {
    expect(
      approvalCardHeading({
        requestType: "Overtime",
        summary: {
          requestDate: "2026-09-20",
          overtime: { workDate: "2026-09-20", startTime: "18:00:00", durationMinutes: 120 },
        },
      })
    ).toEqual({ heading: "2 sa 0 dk", subtitle: "Fazla mesai" });
  });

  it("falls back to the type label alone (no subtitle) for an expense task with no summary", () => {
    expect(approvalCardHeading({ requestType: "ExpenseClaim", summary: null })).toEqual({
      heading: "Masraf",
      subtitle: null,
    });
  });

  it("falls back to the type label alone (no subtitle) for an advance task with no summary", () => {
    expect(approvalCardHeading({ requestType: "Advance", summary: null })).toEqual({
      heading: "Avans",
      subtitle: null,
    });
  });

  it("uses the requested times as heading and 'Eksik giriş/çıkış' as subtitle (FG-004)", () => {
    expect(
      approvalCardHeading({
        requestType: "PunchCorrection",
        summary: {
          requestDate: "2026-09-20",
          punchCorrection: {
            date: "2026-09-18",
            requestedCheckIn: "08:55:00",
            requestedCheckOut: "18:00:00",
            reason: "Turnike arızalıydı",
          },
        },
      })
    ).toEqual({ heading: "08:55 – 18:00", subtitle: "Eksik giriş/çıkış" });
  });
});

describe("approvalDetailRows (görev 8: Başlangıç/Bitiş/Tutar)", () => {
  it("returns start/end date rows for a leave task", () => {
    const rows = approvalDetailRows({
      summary: {
        requestDate: "2026-09-20",
        leave: {
          startDate: "2026-10-01",
          endDate: "2026-10-05",
          leaveTypeCode: "annual",
          days: 5,
        },
      },
    });

    expect(rows).toEqual([
      { label: "Başlangıç", value: expect.any(String) },
      { label: "Bitiş", value: expect.any(String) },
    ]);
  });

  it("returns work-date/duration rows for an overtime task", () => {
    const rows = approvalDetailRows({
      summary: {
        requestDate: "2026-09-20",
        overtime: { workDate: "2026-09-20", startTime: "18:00:00", durationMinutes: 90 },
      },
    });

    expect(rows[0].label).toBe("Başlangıç");
    expect(rows[1]).toEqual({ label: "Tutar", value: "1 sa 30 dk" });
  });

  it("returns no rows when the task has no summary yet", () => {
    expect(approvalDetailRows({ summary: null } as Pick<InboxTask, "summary">)).toEqual([]);
  });

  it("returns day/current-punch rows for a punch-correction task (FG-004)", () => {
    const rows = approvalDetailRows({
      summary: {
        requestDate: "2026-09-20",
        punchCorrection: {
          date: "2026-09-18",
          requestedCheckOut: "18:00:00",
          currentCheckIn: "09:00:00",
          reason: "Turnike arızalıydı",
        },
      },
    });

    expect(rows).toEqual([
      { label: "Gün", value: expect.any(String) },
      { label: "Mevcut", value: "09:00 – —" },
    ]);
  });
});
