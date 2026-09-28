import i18n from "@/lib/i18n";

/**
 * TK-031: text (active language) for `OvertimeSubmissionResult.warnings` codes (`OvertimeWarnings`
 * in `OvertimeRequestFeatures.cs`) - mirrors `web/public/locales/*\/leave.json`'s
 * `overtime.warnings.*`; keys under `requests:overtime.warnings`.
 * `overtime_daily_limit_exceeded` and `overtime_past_date_exceeded` (senseik-042) aren't in that web
 * JSON yet either - checked directly - so their text is this app's own, not copied from a source
 * that doesn't have it.
 */
const WARNING_CODE_PREFIX = "leave.warnings.";

/** Falls back to the raw warning code when unmapped (a newer backend warning this file hasn't caught up with). */
export function overtimeWarningLabel(code: string): string {
  if (!code.startsWith(WARNING_CODE_PREFIX)) return code;
  const key = `requests:overtime.warnings.${code.slice(WARNING_CODE_PREFIX.length)}`;
  return i18n.exists(key) ? i18n.t(key) : code;
}
