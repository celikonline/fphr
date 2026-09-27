import {
  approvalAmountText,
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
    expect(approvalTypeCaption({ requestType: "Expense", summary: null })).toBe("Harcama");
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
});
