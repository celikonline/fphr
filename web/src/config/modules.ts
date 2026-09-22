/**
 * senseik-057 "Ayarlar → Modüller": web mirror of `ProductModules` (Sense.Shared.Contracts/Modules/ProductModules.cs).
 * Core modules can never be turned off; toggleable ones can be disabled per tenant (`/identity/me` →
 * `features.disabledModules`). The server enforces the same rule (ModuleAccessFilter → 403 `module.disabled`);
 * everything here is UI hiding only.
 */
export const CORE_MODULE_KEYS = [
  "identity",
  "organization",
  "employee",
  "workflow",
  "notifications",
  "documents",
  "dataTransfer",
  "reporting",
  "analytics",
  "integrations",
] as const;

export const TOGGLEABLE_MODULE_KEYS = [
  "leave",
  "attendance",
  "payroll",
  "recruitment",
  "expense",
  "assets",
  "performance",
  "announcements",
  "occupationalSafety",
  "training",
  "onboarding",
] as const;

export type CoreModuleKey = (typeof CORE_MODULE_KEYS)[number];
export type ToggleableModuleKey = (typeof TOGGLEABLE_MODULE_KEYS)[number];
export type ProductModuleKey = CoreModuleKey | ToggleableModuleKey;

/**
 * App route prefixes owned by each toggleable module (checked against `/app/...` pathnames, longest match wins).
 * A route under a disabled module renders the "Bu modül kapalı" page; nav/launcher/settings entries whose `path`
 * falls under one of these prefixes are hidden. Paths not listed here belong to core modules and are never hidden.
 */
const MODULE_ROUTE_PREFIXES: ReadonlyArray<readonly [string, ToggleableModuleKey]> = [
  ["/app/leave", "leave"],
  ["/app/settings/leave", "leave"],
  ["/app/attendance", "attendance"],
  ["/app/settings/rules/work-calendars", "attendance"],
  ["/app/payroll", "payroll"],
  ["/app/recruitment", "recruitment"],
  ["/app/settings/recruitment", "recruitment"],
  ["/app/payments", "expense"],
  ["/app/expenses", "expense"],
  ["/app/settings/company/expense-categories", "expense"],
  ["/app/settings/company/asset-categories", "assets"],
  ["/app/performance", "performance"],
  ["/app/company-calendar", "announcements"],
  ["/app/announcements", "announcements"],
  ["/app/onboarding", "onboarding"],
  ["/app/ohs", "occupationalSafety"],
  ["/app/training", "training"],
  ["/app/assets", "assets"],
  ["/app/settings/company/ohs-hazard-classification", "occupationalSafety"],
  ["/app/settings/company/ohs-risk-assessments", "occupationalSafety"],
];

const SORTED_PREFIXES = [...MODULE_ROUTE_PREFIXES].sort((a, b) => b[0].length - a[0].length);

/** Toggleable module that owns this app path, or null for core routes. Matches whole segments only. */
export function moduleForPath(pathname: string): ToggleableModuleKey | null {
  const match = SORTED_PREFIXES.find(
    ([prefix]) => pathname === prefix || pathname.startsWith(`${prefix}/`)
  );
  return match ? match[1] : null;
}

export function isToggleableModule(key: string): key is ToggleableModuleKey {
  return (TOGGLEABLE_MODULE_KEYS as readonly string[]).includes(key);
}
