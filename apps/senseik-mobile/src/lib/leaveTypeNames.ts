/**
 * Turkish names of the seeded leave-type resource keys (mirror of `leave.type.*` in
 * SharedResource.resx; the mobile app has no i18n and is Turkish-only). Several backend endpoints
 * return these keys raw (`/leave/me/balances`, `/leave/me/leave-types`, the workflow inbox's
 * leave summary) - shared here so every screen that renders a leave type name resolves it the same
 * way (B5: previously only `LeaveService` did this, so `ApprovalsScreen`'s inbox summary showed the
 * raw key).
 */
const LEAVE_TYPE_NAMES: Readonly<Record<string, string>> = {
  "leave.type.annual": "Yıllık izin",
  "leave.type.excuse": "Mazeret izni",
  "leave.type.sick": "Hastalık izni",
  "leave.type.job_search": "Yeni iş arama izni",
  "leave.type.marriage": "Evlilik izni",
  "leave.type.bereavement": "Ölüm izni",
  "leave.type.paternity": "Babalık izni",
  "leave.type.maternity": "Doğum izni",
  "leave.type.postnatal_unpaid": "Doğum sonrası ücretsiz izin",
  "leave.type.paid_military_service": "Bedelli askerlik izni",
  "leave.type.unpaid": "Ücretsiz izin",
  "leave.type.foster_care": "Koruyucu aile izni",
  "leave.type.breastfeeding": "Süt izni",
  "leave.type.disabled_child": "Engelli/süreğen hastalıklı çocuk izni",
  "leave.type.template.remote_work": "Uzaktan Çalışma",
  "leave.type.template.birthday": "Doğum günü izni",
};

/** Resolves a seeded leave-type resource key to its Turkish name; free-form (user-entered) names pass through. */
export function leaveTypeDisplayName(name: string): string {
  return LEAVE_TYPE_NAMES[name] ?? name;
}

/**
 * Last-resort fallback for when there is no `leaveTypeName` at all (e.g. the inbox's
 * `InboxTaskSummary.leave` may omit it): humanizes the `LeaveTypeCode` value itself
 * ("postnatalUnpaid" → "Postnatal Unpaid") instead of showing a bare code or nothing. Not a full
 * Turkish translation - callers should prefer `leaveTypeDisplayName` on `leaveTypeName` whenever
 * it is present.
 */
export function humanizeLeaveTypeCode(code: string): string {
  const spaced = code.replace(/([a-z0-9])([A-Z])/g, "$1 $2");
  return spaced.length > 0 ? spaced.charAt(0).toUpperCase() + spaced.slice(1) : spaced;
}
