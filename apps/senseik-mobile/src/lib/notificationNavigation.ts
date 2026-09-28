import type { NavigationProp } from "@react-navigation/native";
import type { AppStackParamList, MainTabsParamList } from "@/types/navigation";

/**
 * Where a notification tap may land: a stack screen by name, or one of the two bottom tabs.
 * `MainTabs` (the tab container itself), `NewRequest` (needs a `kind` param) and `RequestDetail`
 * (needs an `approvalRequestId` - see `resolveRequestDetailTarget` below) are never targets here.
 */
export type NavigationTarget =
  | { stack: Exclude<keyof AppStackParamList, "MainTabs" | "NewRequest" | "RequestDetail"> }
  | { tab: keyof MainTabsParamList };

/**
 * N1: maps an in-app notification's `templateKey` (never its `actionUrl`) to a mobile screen.
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
const NOTIFICATION_TEMPLATE_TARGETS: Record<string, NavigationTarget> = {
  "approval.task_assigned": { stack: "Approvals" },
  "payroll.payslip_ready": { stack: "Payroll" },
  "employee.birthday": { tab: "Home" },
  "announcement.published": { tab: "Home" },
  "leave.added_on_behalf": { stack: "Leave" },
};

/**
 * Resolves where a notification should open when tapped, or `null` when its `templateKey` names no
 * known mobile screen. `actionUrl` is intentionally not read here - see module doc comment. Own
 * keys only, so a key like "constructor" never resolves to an `Object.prototype` member.
 */
export function resolveNotificationNavigationTarget(templateKey: string): NavigationTarget | null {
  return Object.prototype.hasOwnProperty.call(NOTIFICATION_TEMPLATE_TARGETS, templateKey)
    ? NOTIFICATION_TEMPLATE_TARGETS[templateKey]
    : null;
}

const REQUEST_DETAIL_ACTION_URL_PATTERN =
  /^\/app\/requests\/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$/;

/**
 * FG-009: `request.comment_added`'ın tek olası hedefi var - o anki `approvalRequestId` - bu yüzden
 * (yukarıdaki "yalnız templateKey, asla actionUrl" kuralının kasıtlı tek istisnası olarak)
 * `actionUrl`'den GUID'i okur. `RequestDetail`'in navigasyon hedefi `NavigationTarget` union'ına
 * (parametresiz stack/tab) sığmadığı için ayrı döner; çağıran `templateKey ===
 * "request.comment_added"` olduğunda bunu dener. Yalnız `/app/requests/{guid}` biçimini kabul eder
 * - server bunu üretir (P06 backend raporu); başka her actionUrl (biçimsiz, boş, farklı yol) `null`
 * döner ve hiçbir yere gidilmez.
 */
export function resolveRequestDetailTarget(
  actionUrl: string | null | undefined
): { approvalRequestId: string } | null {
  if (!actionUrl) return null;
  const match = REQUEST_DETAIL_ACTION_URL_PATTERN.exec(actionUrl);
  return match ? { approvalRequestId: match[1] } : null;
}

/** Opens a resolved target: stack screens directly, tabs through the `MainTabs` container. */
export function navigateToTarget(
  navigation: NavigationProp<AppStackParamList>,
  target: NavigationTarget
): void {
  if ("tab" in target) {
    navigation.navigate("MainTabs", { screen: target.tab });
    return;
  }
  navigation.navigate(target.stack);
}
