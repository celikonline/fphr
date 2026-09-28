import { overtimeWarningLabel } from "@/lib/overtimeWarnings";

describe("overtimeWarningLabel (TK-031)", () => {
  it("translates a known warning code", () => {
    expect(overtimeWarningLabel("leave.warnings.overtime_consent_missing")).toBe(
      "Yazılı fazla çalışma onayınız kayıtlı değil."
    );
  });

  it("translates every OvertimeWarnings code (OvertimeRequestFeatures.cs) to non-raw text", () => {
    const codes = [
      "leave.warnings.overtime_consent_missing",
      "leave.warnings.overtime_night_consent_missing",
      "leave.warnings.overtime_overlaps_schedule",
      "leave.warnings.overtime_annual_limit_exceeded",
      "leave.warnings.overtime_daily_limit_exceeded",
      "leave.warnings.overtime_past_date_exceeded",
    ];
    for (const code of codes) {
      expect(overtimeWarningLabel(code)).not.toBe(code);
    }
  });

  it("falls back to the raw code when unmapped", () => {
    expect(overtimeWarningLabel("leave.warnings.something_new")).toBe(
      "leave.warnings.something_new"
    );
  });
});
