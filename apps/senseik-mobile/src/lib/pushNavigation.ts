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
 * `data.type` -> tab screen, for template keys that don't carry an explicit `data.screen`.
 * Keys mirror `NotificationTemplateKeys` (Notifications.Contracts). The mobile app has no
 * dedicated announcement screen yet, so `announcement.published` lands on Dashboard, the closest
 * general landing screen; `employee.anniversary` is included for forward-compatibility even
 * though the backend catalog does not define it yet (only `employee.birthday` exists today).
 */
const NOTIFICATION_TYPE_SCREENS: Record<string, string> = {
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
