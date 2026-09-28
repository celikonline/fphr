import type { NavigationTarget } from "@/lib/notificationNavigation";

/**
 * Maps a push notification's `data` payload to a navigation target, for TK-025.
 *
 * Two payload shapes are supported:
 * - `data.screen`: the sender names a screen directly (existing behavior; the names are the old
 *   tab names, which now resolve to stack screens or the Home tab).
 * - `data.type`: the sender names a backend notification template key (see
 *   `NotificationTemplateKeys` in `Sense.Modules.Notifications.Contracts`) and we resolve it to a
 *   target here. This covers content-type notifications (doğum günü, yıl dönümü, duyuru) whose
 *   sender may not know about mobile screen names.
 *
 * Only known targets are ever returned: a compromised or misconfigured sender can't deep-link
 * into an arbitrary route.
 */

/** Screen names a push notification may request, and where each one lands. Anything else is
 * ignored. "Dashboard" is the pre-redesign name of the Home tab. */
export const ALLOWED_PUSH_SCREENS: Record<string, NavigationTarget> = {
  Dashboard: { tab: "Home" },
  Attendance: { stack: "Attendance" },
  Leave: { stack: "Leave" },
  Payroll: { stack: "Payroll" },
  Approvals: { stack: "Approvals" },
  Profile: { stack: "Profile" },
};

/**
 * `data.type` -> target, for template keys that don't carry an explicit `data.screen`. This is
 * the push-delivery counterpart of `NOTIFICATION_TEMPLATE_TARGETS` in `notificationNavigation.ts`
 * (in-app list) - kept aligned with it so a template that navigates from the in-app notification
 * center navigates the same way from a push tap (see push-bildirimleri-tasarim.md §3.5: the backend
 * sends push `data` as `{ type: <templateKey>, id }`). `employee.anniversary` is included for
 * forward-compatibility even though the backend catalog does not define it yet (only
 * `employee.birthday` exists today); `request.*` keys are intentionally not mapped here either,
 * mirroring `notificationNavigation.ts` (a "request" can be Leave, Advance, Expense, Overtime,
 * Requisition or AssetAssignment - types mobile has no single screen for).
 */
const NOTIFICATION_TYPE_TARGETS: Record<string, NavigationTarget> = {
  "approval.task_assigned": { stack: "Approvals" },
  "payroll.payslip_ready": { stack: "Payroll" },
  "leave.added_on_behalf": { stack: "Leave" },
  "employee.birthday": { tab: "Home" },
  "employee.anniversary": { tab: "Home" },
  "announcement.published": { tab: "Home" },
};

/**
 * Resolves where a push notification's `data` payload should open, or `null` when the payload
 * names no known screen/type. Own keys only, so "constructor"/"toString" never resolve to an
 * `Object.prototype` member.
 */
export function resolvePushNavigationScreen(
  data: Record<string, unknown> | undefined
): NavigationTarget | null {
  const screen = data?.screen;
  if (
    typeof screen === "string" &&
    Object.prototype.hasOwnProperty.call(ALLOWED_PUSH_SCREENS, screen)
  ) {
    return ALLOWED_PUSH_SCREENS[screen];
  }

  const type = data?.type;
  if (
    typeof type === "string" &&
    Object.prototype.hasOwnProperty.call(NOTIFICATION_TYPE_TARGETS, type)
  ) {
    return NOTIFICATION_TYPE_TARGETS[type];
  }

  return null;
}
