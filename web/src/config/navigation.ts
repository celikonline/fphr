import type { LucideIcon } from "lucide-react";
import {
  AppWindow,
  BarChart3,
  Building2,
  CalendarDays,
  CalendarRange,
  ClipboardList,
  Clock,
  CreditCard,
  FileSpreadsheet,
  FileText,
  GitBranch,
  Home,
  Inbox,
  KeyRound,
  LifeBuoy,
  ListChecks,
  MapPin,
  Network,
  Palmtree,
  PieChart,
  ScrollText,
  Settings,
  ShieldCheck,
  Target,
  UserCircle,
  UserPlus,
  Users,
  Wallet,
  Workflow,
  CalendarClock,
  Stamp,
} from "lucide-react";
import type { AccessSettingKey } from "@/types";

export interface NavItem {
  key: string;
  /** i18n key in the "navigation" namespace. */
  labelKey: string;
  path: string;
  icon: LucideIcon;
  /** At least one of these permissions is required to see the item (empty = always visible). */
  permissions?: string[];
  /** Hidden unless this effective access setting (from /identity/me `features`) is on. */
  accessSetting?: AccessSettingKey;
  /** Planned roadmap phase for features that are not built yet (renders the coming-soon page). */
  phase?: number;
  /** Match the path exactly for active highlighting (e.g. the /app home link). */
  end?: boolean;
}

/** Settings hub card permissions, reused so the "Ayarlar" menu entry shows whenever any card would. */
export const SETTINGS_PERMISSIONS = {
  structure: ["organization.units.read"],
  holidayCalendars: ["organization.holiday_calendars.read"],
  locations: ["organization.locations.read"],
  companyProfile: ["identity.tenant.view", "identity.tenant.manage"],
  tenantManage: ["identity.tenant.manage"],
  access: ["identity.tenant.view", "identity.tenant.manage"],
  usersRoles: ["identity.users.view", "identity.roles.view"],
  auditLog: ["identity.audit.view"],
  supportAccess: ["identity.impersonation.approve"],
  dataTransfer: ["datatransfer.import.view"],
  workflowPolicies: ["workflow.policies.read"],
  customFields: ["employee.customfield.view"],
} as const satisfies Record<string, readonly string[]>;

const ANY_SETTINGS_PERMISSION = Array.from(new Set(Object.values(SETTINGS_PERMISSIONS).flat()));

/** Single left sidebar, in Kolay İK order (11.3.13). */
export const NAV_ITEMS: NavItem[] = [
  { key: "home", labelKey: "nav.home", path: "/app", icon: Home, end: true },
  {
    key: "employees",
    labelKey: "nav.employees",
    path: "/app/employees",
    icon: Users,
    permissions: ["employee.employees.read", "employee.directory.view"],
  },
  {
    key: "leaves",
    labelKey: "nav.leaves",
    path: "/app/leave",
    icon: Palmtree,
    permissions: ["leave.my.balances", "leave.my.requests"],
    end: true,
  },
  {
    key: "calendar",
    labelKey: "nav.calendar",
    path: "/app/leave/calendar",
    icon: CalendarDays,
    permissions: ["leave.my.calendar"],
    accessSetting: "showCalendarMenu",
  },
  { key: "overtimes", labelKey: "nav.overtimes", path: "/app/leave/overtimes", icon: Clock, permissions: ["leave.my.overtimes"] },
  {
    key: "leaveRules",
    labelKey: "nav.leaveRules",
    path: "/app/settings/leave/rule-sets",
    icon: ScrollText,
    permissions: ["leave.rule_sets.read"],
  },
  { key: "requests", labelKey: "nav.requests", path: "/app/requests", icon: ClipboardList, phase: 1 },
  { key: "apps", labelKey: "nav.apps", path: "/app/apps", icon: AppWindow },
  { key: "reports", labelKey: "nav.reports", path: "/app/reports", icon: BarChart3, phase: 4 },
  { key: "settings", labelKey: "nav.settings", path: "/app/settings", icon: Settings, permissions: ANY_SETTINGS_PERMISSION },
];

export const NAV_FOOTER_ITEMS: NavItem[] = [
  { key: "account", labelKey: "nav.account", path: "/app/account", icon: UserCircle },
  { key: "support", labelKey: "nav.support", path: "/app/support", icon: LifeBuoy, phase: 4 },
];

/** "Uygulamalar" launcher entries (header dropdown + /app/apps). Entries with a phase are shown disabled. */
export const APP_LAUNCHER_ITEMS: NavItem[] = [
  {
    key: "orgChart",
    labelKey: "apps.orgChart",
    path: "/app/employees/org-chart",
    icon: GitBranch,
    permissions: ["employee.orgchart.view"],
    accessSetting: "showOrgChart",
  },
  {
    key: "dataTransfer",
    labelKey: "apps.dataTransfer",
    path: "/app/settings/data-transfer",
    icon: FileSpreadsheet,
    permissions: [...SETTINGS_PERMISSIONS.dataTransfer],
  },
  {
    key: "workflowInbox",
    labelKey: "apps.workflowInbox",
    path: "/app/inbox",
    icon: Inbox,
    permissions: ["workflow.inbox.view"],
  },
  { key: "overtime", labelKey: "apps.overtime", path: "/app/leave/overtimes", icon: Clock, permissions: ["leave.my.overtimes"] },
  { key: "timesheet", labelKey: "apps.timesheet", path: "/app/apps", icon: CalendarClock, phase: 2 },
  { key: "shifts", labelKey: "apps.shifts", path: "/app/apps", icon: CalendarRange, phase: 2 },
  { key: "payroll", labelKey: "apps.payroll", path: "/app/apps", icon: Wallet, phase: 3 },
  { key: "expenses", labelKey: "apps.expenses", path: "/app/apps", icon: FileText, phase: 4 },
  { key: "performance", labelKey: "apps.performance", path: "/app/apps", icon: Target, phase: 5 },
  { key: "recruitment", labelKey: "apps.recruitment", path: "/app/apps", icon: UserPlus, phase: 6 },
  { key: "analytics", labelKey: "apps.analytics", path: "/app/apps", icon: PieChart, phase: 7 },
];

export interface SettingsLink {
  key: string;
  /** i18n keys in the "navigation" namespace. */
  labelKey: string;
  descriptionKey: string;
  path: string;
  icon: LucideIcon;
  permissions?: readonly string[];
  phase?: number;
}

export interface SettingsSection {
  key: string;
  labelKey: string;
  path?: string;
  links: SettingsLink[];
}

/** Ayarlar hub tree (11.3.13 "Ayarlar ağacı"). */
export const SETTINGS_SECTIONS: SettingsSection[] = [
  {
    key: "company",
    labelKey: "settings.company",
    links: [
      {
        key: "structure",
        labelKey: "settings.structure",
        descriptionKey: "settings.structureDesc",
        path: "/app/settings/company/structure",
        icon: Network,
        permissions: SETTINGS_PERMISSIONS.structure,
      },
      {
        key: "holidayCalendars",
        labelKey: "settings.holidayCalendars",
        descriptionKey: "settings.holidayCalendarsDesc",
        path: "/app/settings/company/holiday-calendars",
        icon: CalendarDays,
        permissions: SETTINGS_PERMISSIONS.holidayCalendars,
      },
      {
        key: "locations",
        labelKey: "settings.locations",
        descriptionKey: "settings.locationsDesc",
        path: "/app/settings/company/locations",
        icon: MapPin,
        permissions: SETTINGS_PERMISSIONS.locations,
      },
      {
        key: "companyProfile",
        labelKey: "settings.companyProfile",
        descriptionKey: "settings.companyProfileDesc",
        path: "/app/settings/company/profile",
        icon: Building2,
        permissions: SETTINGS_PERMISSIONS.companyProfile,
      },
      {
        key: "approvalFlows",
        labelKey: "settings.approvalFlows",
        descriptionKey: "settings.approvalFlowsDesc",
        path: "/app/settings/company/approval-flows",
        icon: Workflow,
        permissions: SETTINGS_PERMISSIONS.workflowPolicies,
      },
      {
        key: "customFields",
        labelKey: "settings.customFields",
        descriptionKey: "settings.customFieldsDesc",
        path: "/app/settings/company/custom-fields",
        icon: ListChecks,
        permissions: SETTINGS_PERMISSIONS.customFields,
      },
    ],
  },
  {
    key: "rules",
    labelKey: "settings.rules",
    path: "/app/settings/rules",
    links: [
      {
        key: "workCalendars",
        labelKey: "settings.workCalendars",
        descriptionKey: "settings.workCalendarsDesc",
        path: "/app/settings/rules/work-calendars",
        icon: CalendarClock,
        permissions: SETTINGS_PERMISSIONS.tenantManage,
        phase: 1,
      },
      {
        key: "visaProcesses",
        labelKey: "settings.visaProcesses",
        descriptionKey: "settings.visaProcessesDesc",
        path: "/app/settings/rules/visa-processes",
        icon: Stamp,
        permissions: SETTINGS_PERMISSIONS.tenantManage,
        phase: 1,
      },
    ],
  },
  {
    key: "system",
    labelKey: "settings.system",
    links: [
      {
        key: "access",
        labelKey: "settings.access",
        descriptionKey: "settings.accessDesc",
        path: "/app/settings/system/access",
        icon: KeyRound,
        permissions: SETTINGS_PERMISSIONS.access,
      },
      {
        key: "usersRoles",
        labelKey: "settings.usersRoles",
        descriptionKey: "settings.usersRolesDesc",
        path: "/app/settings/system/users-roles",
        icon: Users,
        permissions: SETTINGS_PERMISSIONS.usersRoles,
      },
      {
        key: "auditLog",
        labelKey: "settings.auditLog",
        descriptionKey: "settings.auditLogDesc",
        path: "/app/settings/system/audit-log",
        icon: ScrollText,
        permissions: SETTINGS_PERMISSIONS.auditLog,
      },
      {
        key: "supportAccess",
        labelKey: "settings.supportAccess",
        descriptionKey: "settings.supportAccessDesc",
        path: "/app/settings/system/support-access",
        icon: ShieldCheck,
        permissions: SETTINGS_PERMISSIONS.supportAccess,
      },
      {
        key: "billing",
        labelKey: "settings.billing",
        descriptionKey: "settings.billingDesc",
        path: "/app/settings/system/billing",
        icon: CreditCard,
        permissions: SETTINGS_PERMISSIONS.tenantManage,
        phase: 8,
      },
    ],
  },
  {
    key: "dataTransfer",
    labelKey: "settings.dataTransferSection",
    links: [
      {
        key: "dataTransfer",
        labelKey: "settings.dataTransfer",
        descriptionKey: "settings.dataTransferDesc",
        path: "/app/settings/data-transfer",
        icon: FileSpreadsheet,
        permissions: SETTINGS_PERMISSIONS.dataTransfer,
      },
    ],
  },
];

export interface BreadcrumbRoute {
  /** Route pattern; `:param` segments match any value. */
  path: string;
  labelKey: string;
  /** false for grouping segments that have no page of their own. */
  link?: boolean;
}

/** Labels for every routed path prefix under /app (the home crumb is always rendered first). */
export const BREADCRUMB_ROUTES: BreadcrumbRoute[] = [
  ...NAV_ITEMS.filter((item) => item.path !== "/app").map((item) => ({ path: item.path, labelKey: item.labelKey })),
  ...NAV_FOOTER_ITEMS.map((item) => ({ path: item.path, labelKey: item.labelKey })),
  { path: "/app/employees/org-chart", labelKey: "nav.orgChart" },
  { path: "/app/employees/:id", labelKey: "nav.employeeCard" },
  { path: "/app/settings/company", labelKey: "settings.company", link: false },
  { path: "/app/settings/system", labelKey: "settings.system", link: false },
  { path: "/app/settings/data-transfer/jobs", labelKey: "settings.importJobs", link: false },
  { path: "/app/settings/data-transfer/jobs/:id", labelKey: "settings.importJob" },
  ...SETTINGS_SECTIONS.flatMap((section) => [
    ...(section.path ? [{ path: section.path, labelKey: section.labelKey }] : []),
    ...section.links.map((link) => ({ path: link.path, labelKey: link.labelKey })),
  ]),
];

/** Resolves breadcrumbs for a pathname: one entry per matching prefix (literal routes win over `:param`). */
export function resolveBreadcrumbs(pathname: string): { path: string; labelKey: string; link: boolean }[] {
  const segments = pathname.replace(/\/+$/, "").split("/").filter(Boolean);
  const crumbs: { path: string; labelKey: string; link: boolean }[] = [];
  for (let i = 2; i <= segments.length; i++) {
    const prefix = segments.slice(0, i);
    const path = `/${prefix.join("/")}`;
    const literal = BREADCRUMB_ROUTES.find((route) => route.path === path);
    const route =
      literal ??
      BREADCRUMB_ROUTES.find((candidate) => {
        const parts = candidate.path.split("/").filter(Boolean);
        return parts.length === prefix.length && parts.every((part, idx) => part.startsWith(":") || part === prefix[idx]);
      });
    if (route) {
      crumbs.push({ path, labelKey: route.labelKey, link: route.link !== false });
    }
  }
  return crumbs;
}
