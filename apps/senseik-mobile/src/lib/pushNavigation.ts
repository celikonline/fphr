/**
 * Maps a push notification's `data` payload to a tab screen name, for TK-025.
 *
 * Two payload shapes are supported:
 * - `data.screen`: the sender names a tab directly (existing behavior).
 * - `data.type`: the sender names a backend notification template key (see
 *   `NotificationTemplateKeys` in `Sense.Modules.Notifications.Contracts`) and we resolve it to a
 *   screen here. This covers content-type notifications (doğum günü, yıl dönümü, duyuru) whose
 *   sender may not know about mobile tab names.
 *
 * Only known tab screens are ever returned: a compromised or misconfigured sender can't deep-link
 * into an arbitrary route.
 */

/** Tabs a push notification may request. Anything else is ignored. */
export const ALLOWED_PUSH_SCREENS = new Set([
  "Dashboard",
  "Attendance",
  "Leave",
  "Payroll",
  "Approvals",
  "Profile",
]);

/**
 * `data.type` -> tab screen, for template keys that don't carry an explicit `data.screen`. This is
 * the push-delivery counterpart of `NOTIFICATION_TEMPLATE_SCREENS` in `notificationNavigation.ts`
 * (in-app list) - kept aligned with it so a template that navigates from the in-app notification
 * center navigates the same way from a push tap (see push-bildirimleri-tasarim.md §3.5: the backend
 * sends push `data` as `{ type: <templateKey>, id }`). `employee.anniversary` is included for
 * forward-compatibility even though the backend catalog does not define it yet (only
 * `employee.birthday` exists today); `request.*` keys are intentionally not mapped here either,
 * mirroring `notificationNavigation.ts` (a "request" can be Leave, Advance, Expense, Overtime,
 * Requisition or AssetAssignment - types mobile has no single screen for).
 */
const NOTIFICATION_TYPE_SCREENS: Record<string, string> = {
  "approval.task_assigned": "Approvals",
  "payroll.payslip_ready": "Payroll",
  "leave.added_on_behalf": "Leave",
  "employee.birthday": "Dashboard",
  "employee.anniversary": "Dashboard",
  "announcement.published": "Dashboard",
};

/**
 * Resolves the tab screen a push notification's `data` payload should open, or `null` when the
 * payload names no known screen/type.
 */
export function resolvePushNavigationScreen(
  data: Record<string, unknown> | undefined
): string | null {
  const screen = data?.screen;
  if (typeof screen === "string" && ALLOWED_PUSH_SCREENS.has(screen)) {
    return screen;
  }

  const type = data?.type;
  if (typeof type === "string" && type in NOTIFICATION_TYPE_SCREENS) {
    return NOTIFICATION_TYPE_SCREENS[type];
  }

  return null;
}
