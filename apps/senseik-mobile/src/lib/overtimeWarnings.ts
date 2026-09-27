/**
 * TK-031: Turkish text for `OvertimeSubmissionResult.warnings` codes (`OvertimeWarnings` in
 * `OvertimeRequestFeatures.cs`) - mirrors `web/public/locales/tr/leave.json`'s `overtime.warnings.*`.
 * `overtime_daily_limit_exceeded` and `overtime_past_date_exceeded` (senseik-042) aren't in that web
 * JSON yet either - checked directly - so their text below is this file's own, not copied from a
 * source that doesn't have it.
 */
const OVERTIME_WARNING_LABELS: Record<string, string> = {
  "leave.warnings.overtime_consent_missing": "Yazılı fazla çalışma onayınız kayıtlı değil.",
  "leave.warnings.overtime_night_consent_missing":
    "Kayıt gece saatlerine denk geliyor ancak gece fazla çalışma onayınız yok.",
  "leave.warnings.overtime_overlaps_schedule":
    "Talebin bir kısmı planlı çalışma saatleri içinde; o kısım ödenecek süreden düşüldü.",
  "leave.warnings.overtime_annual_limit_exceeded":
    "Bu kayıtla yıllık fazla mesai toplamı yasal sınırı aşıyor.",
  "leave.warnings.overtime_daily_limit_exceeded":
    "Bu kayıtla günlük fazla mesai toplamı 11 saati aşıyor.",
  "leave.warnings.overtime_past_date_exceeded": "Bu tarih 7 günden daha eski bir geçmiş tarih.",
};

/** Falls back to the raw warning code when unmapped (a newer backend warning this file hasn't caught up with). */
export function overtimeWarningLabel(code: string): string {
  return OVERTIME_WARNING_LABELS[code] ?? code;
}
