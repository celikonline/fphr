import i18n from "@/lib/i18n";

/**
 * Names of the seeded leave-type resource keys (mirror of `leave.type.*` in SharedResource.resx),
 * in the active language - `leave:types.*` in `src/locales`. Several backend endpoints return these
 * keys raw (`/leave/me/balances`, `/leave/me/leave-types`, the workflow inbox's leave summary) -
 * shared here so every screen that renders a leave type name resolves it the same way (B5:
 * previously only `LeaveService` did this, so `ApprovalsScreen`'s inbox summary showed the raw key).
 */
const RESOURCE_KEY_PREFIX = "leave.type.";

/** Resolves a seeded leave-type resource key to its name in the active language; free-form (user-entered) names pass through. */
export function leaveTypeDisplayName(name: string): string {
  if (!name.startsWith(RESOURCE_KEY_PREFIX)) return name;
  const key = `leave:types.${name.slice(RESOURCE_KEY_PREFIX.length)}`;
  return i18n.exists(key) ? i18n.t(key) : name;
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
