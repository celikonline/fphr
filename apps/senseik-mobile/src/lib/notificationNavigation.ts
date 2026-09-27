import type { MainTabsParamList } from "@/types/navigation";

/**
 * N1: maps an in-app notification's `templateKey` (never its `actionUrl`) to a tab screen name.
 *
 * Mirrors `resolvePushNavigationScreen` (`pushNavigation.ts`): a notification's `actionUrl` is a
 * server-controlled string ("/app/onboarding", web-only paths like "/app/inbox/..."), but the
 * mobile app has a different, smaller set of screens than the web app, and blindly `navigate`-ing
 * to whatever the payload says would let a misconfigured or compromised template deep-link
 * somewhere unintended. Only `templateKey`s with a single, unambiguous mobile destination are
 * mapped; everything else (including every `request.*` key, since a "request" can be a Leave,
 * Advance, Expense, Overtime, Requisition or AssetAssignment - types mobile has no screen for) opens
 * nothing, matching this project's "unknown -> no navigation" convention.
 */
const NOTIFICATION_TEMPLATE_SCREENS: Record<string, keyof MainTabsParamList> = {
  "approval.task_assigned": "Approvals",
  "payroll.payslip_ready": "Payroll",
  "employee.birthday": "Dashboard",
  "announcement.published": "Dashboard",
  "leave.added_on_behalf": "Leave",
};

/**
 * Resolves the tab screen a notification should open when tapped, or `null` when its `templateKey`
 * names no known mobile screen. `actionUrl` is intentionally not read here - see module doc comment.
 */
export function resolveNotificationNavigationTarget(
  templateKey: string
): keyof MainTabsParamList | null {
  return NOTIFICATION_TEMPLATE_SCREENS[templateKey] ?? null;
}
