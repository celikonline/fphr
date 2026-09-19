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
  Landmark,
  LifeBuoy,
  ListChecks,
  LogOut,
  MapPin,
  Network,
  Package,
  Palmtree,
  PieChart,
  Receipt,
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
  CalendarCheck,
  Stamp,
  FileStack,
  FileWarning,
  Upload,
} from "lucide-react";
import type { AccessSettingKey } from "@/types";

/** Corporate palette keys from mantine-theme.ts usable for per-item icon accents. */
export type CorporateColorKey = "brand" | "navy" | "blue" | "cyan" | "teal" | "orange" | "gold" | "steel";

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
  /** Corporate accent color for the icon; falls back to the primary brand color when unset. */
  iconColor?: CorporateColorKey;
}

/** CSS var for a NavItem's icon accent, for use as the lucide icon's `color` prop or a styles override. */
export function navIconColor(item: NavItem): string {
  return item.iconColor ? `var(--mantine-color-${item.iconColor}-6)` : "var(--mantine-primary-color-filled)";
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
  terminationReasons: ["employee.termination_reason.manage"],
  costCenters: ["organization.cost_centers.view"],
  subcontractors: ["employee.subcontractor.manage"],
  documentTypes: ["documents.document_types.view", "documents.document_types.manage"],
  missingDocuments: ["documents.requirements.view"],
  assetCategories: ["assets.categories.view", "assets.categories.manage"],
  expenseCategories: ["expense.categories.manage"],
  recruitmentFormTemplates: ["recruitment.form_templates.read"],
  recruitmentAutomationRules: ["recruitment.automation_rules.read"],
  workSchedules: ["attendance.workschedule.view"],
} as const satisfies Record<string, readonly string[]>;

const ANY_SETTINGS_PERMISSION = Array.from(new Set(Object.values(SETTINGS_PERMISSIONS).flat()));

/** Single left sidebar, in Kolay İK order (11.3.13). */
export const NAV_ITEMS: NavItem[] = [
  { key: "home", labelKey: "nav.home", path: "/app", icon: Home, end: true, iconColor: "navy" },
  {
    key: "employees",
    labelKey: "nav.employees",
    path: "/app/employees",
    icon: Users,
    permissions: ["employee.employees.read", "employee.directory.view"],
    iconColor: "blue",
  },
  {
    key: "leaves",
    labelKey: "nav.leaves",
    path: "/app/leave",
    icon: Palmtree,
    permissions: ["leave.my.balances", "leave.my.requests"],
    end: true,
    iconColor: "teal",
  },
  {
    key: "calendar",
    labelKey: "nav.calendar",
    path: "/app/leave/calendar",
    icon: CalendarDays,
    permissions: ["leave.my.calendar"],
    accessSetting: "showCalendarMenu",
    iconColor: "cyan",
  },
  {
    key: "overtimes",
    labelKey: "nav.overtimes",
    path: "/app/leave/overtimes",
    icon: Clock,
    permissions: ["leave.my.overtimes"],
    iconColor: "orange",
  },
  {
    key: "leaveRules",
    labelKey: "nav.leaveRules",
    path: "/app/settings/leave/rule-sets",
    icon: ScrollText,
    permissions: ["leave.rule_sets.read"],
    iconColor: "steel",
  },
  { key: "requests", labelKey: "nav.requests", path: "/app/requests", icon: ClipboardList, iconColor: "gold" },
  { key: "apps", labelKey: "nav.apps", path: "/app/apps", icon: AppWindow, iconColor: "navy" },
  {
    key: "reports",
    labelKey: "nav.reports",
    path: "/app/reports",
    icon: BarChart3,
    permissions: ["analytics.kpi.view", "reports.catalog.view", "reports.my.executions"],
    iconColor: "blue",
  },
  {
    key: "settings",
    labelKey: "nav.settings",
    path: "/app/settings",
    icon: Settings,
    permissions: ANY_SETTINGS_PERMISSION,
    iconColor: "steel",
  },
  // Appended (senseik-047): previously only reachable via the Uygulamalar launcher / home widget - D-03 keşfedilebilirlik.
  {
    key: "workflowInbox",
    labelKey: "nav.workflowInbox",
    path: "/app/inbox",
    icon: Inbox,
    permissions: ["workflow.inbox.view"],
    iconColor: "teal",
  },
];

export const NAV_FOOTER_ITEMS: NavItem[] = [
  { key: "support", labelKey: "nav.support", path: "/app/support", icon: LifeBuoy, iconColor: "orange" },
  { key: "trust", labelKey: "nav.trust", path: "/app/trust", icon: ShieldCheck, iconColor: "teal" },
  { key: "account", labelKey: "nav.account", path: "/app/account", icon: UserCircle, iconColor: "navy" },
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
    iconColor: "navy",
  },
  {
    key: "dataTransfer",
    labelKey: "apps.dataTransfer",
    path: "/app/settings/data-transfer",
    icon: FileSpreadsheet,
    permissions: [...SETTINGS_PERMISSIONS.dataTransfer],
    iconColor: "blue",
  },
  {
    key: "workflowInbox",
    labelKey: "apps.workflowInbox",
    path: "/app/inbox",
    icon: Inbox,
    permissions: ["workflow.inbox.view"],
    iconColor: "teal",
  },
  {
    key: "overtime",
    labelKey: "apps.overtime",
    path: "/app/leave/overtimes",
    icon: Clock,
    permissions: ["leave.my.overtimes"],
    iconColor: "orange",
  },
  {
    key: "timesheet",
    labelKey: "apps.timesheet",
    path: "/app/attendance/timesheet",
    icon: CalendarClock,
    permissions: ["attendance.my.timesheet"],
    iconColor: "orange",
  },
  {
    key: "shifts",
    labelKey: "apps.shifts",
    path: "/app/attendance/shifts",
    icon: CalendarRange,
    permissions: ["attendance.shift.self"],
    iconColor: "teal",
  },
  {
    key: "teamTimesheet",
    labelKey: "apps.teamTimesheet",
    path: "/app/attendance/team-timesheet",
    icon: Users,
    permissions: ["attendance.timesheet.view"],
    iconColor: "navy",
  },
  {
    key: "shiftPlan",
    labelKey: "apps.shiftPlan",
    path: "/app/attendance/shift-plan",
    icon: CalendarCheck,
    permissions: ["attendance.shift.view"],
    iconColor: "teal",
  },
  {
    key: "deviceLogImport",
    labelKey: "apps.deviceLogImport",
    path: "/app/attendance/device-logs/import",
    icon: Upload,
    permissions: ["attendance.timesheet.import"],
    iconColor: "orange",
  },
  {
    key: "payroll",
    labelKey: "apps.payroll",
    path: "/app/payroll/periods",
    icon: Wallet,
    permissions: ["payroll.period.recalculate"],
    iconColor: "navy",
  },
  {
    key: "expenses",
    labelKey: "apps.expenses",
    path: "/app/payments",
    icon: FileText,
    permissions: ["expense.my.payments"],
    iconColor: "blue",
  },
  {
    key: "expensePaymentsAdmin",
    labelKey: "apps.expensePaymentsAdmin",
    path: "/app/expenses/payments",
    icon: Wallet,
    permissions: ["expense.claims.view", "expense.advances.view"],
    iconColor: "blue",
  },
  { key: "performance", labelKey: "apps.performance", path: "/app/performance/goals", icon: Target, permissions: ["performance.my.goals"], iconColor: "gold" },
  {
    key: "performanceReviewCycles",
    labelKey: "apps.performanceReviewCycles",
    path: "/app/performance/review-cycles",
    icon: CalendarRange,
    permissions: ["performance.my.reviews"],
    iconColor: "gold",
  },
  {
    key: "recruitment",
    labelKey: "apps.recruitment",
    path: "/app/recruitment/postings",
    icon: UserPlus,
    permissions: ["recruitment.postings.read"],
    iconColor: "gold",
  },
  {
    key: "recruitmentCandidates",
    labelKey: "apps.recruitmentCandidates",
    path: "/app/recruitment/candidates",
    icon: Users,
    permissions: ["recruitment.candidates.read"],
    iconColor: "gold",
  },
  {
    key: "recruitmentRequisitions",
    labelKey: "apps.recruitmentRequisitions",
    path: "/app/recruitment/requisitions",
    icon: ClipboardList,
    permissions: ["recruitment.requisitions.read"],
    iconColor: "gold",
  },
  {
    key: "analytics",
    labelKey: "apps.analytics",
    path: "/app/reports",
    icon: PieChart,
    permissions: ["analytics.kpi.view", "reports.catalog.view", "reports.my.executions"],
    iconColor: "blue",
  },
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
      {
        key: "terminationReasons",
        labelKey: "settings.terminationReasons",
        descriptionKey: "settings.terminationReasonsDesc",
        path: "/app/settings/company/termination-reasons",
        icon: LogOut,
        permissions: SETTINGS_PERMISSIONS.terminationReasons,
      },
      {
        key: "costCenters",
        labelKey: "settings.costCenters",
        descriptionKey: "settings.costCentersDesc",
        path: "/app/settings/company/cost-centers",
        icon: Landmark,
        permissions: SETTINGS_PERMISSIONS.costCenters,
      },
      {
        key: "subcontractors",
        labelKey: "settings.subcontractors",
        descriptionKey: "settings.subcontractorsDesc",
        path: "/app/settings/company/subcontractors",
        icon: Building2,
        permissions: SETTINGS_PERMISSIONS.subcontractors,
      },
      {
        key: "documentTypes",
        labelKey: "settings.documentTypes",
        descriptionKey: "settings.documentTypesDesc",
        path: "/app/settings/company/document-types",
        icon: FileStack,
        permissions: SETTINGS_PERMISSIONS.documentTypes,
      },
      {
        key: "missingDocuments",
        labelKey: "settings.missingDocuments",
        descriptionKey: "settings.missingDocumentsDesc",
        path: "/app/settings/company/missing-documents",
        icon: FileWarning,
        permissions: SETTINGS_PERMISSIONS.missingDocuments,
      },
      {
        key: "assetCategories",
        labelKey: "settings.assetCategories",
        descriptionKey: "settings.assetCategoriesDesc",
        path: "/app/settings/company/asset-categories",
        icon: Package,
        permissions: SETTINGS_PERMISSIONS.assetCategories,
      },
      {
        key: "expenseCategories",
        labelKey: "settings.expenseCategories",
        descriptionKey: "settings.expenseCategoriesDesc",
        path: "/app/settings/company/expense-categories",
        icon: Receipt,
        permissions: SETTINGS_PERMISSIONS.expenseCategories,
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
        permissions: SETTINGS_PERMISSIONS.workSchedules,
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
  {
    key: "recruitment",
    labelKey: "settings.recruitmentSection",
    links: [
      {
        key: "recruitmentFormTemplates",
        labelKey: "settings.recruitmentFormTemplates",
        descriptionKey: "settings.recruitmentFormTemplatesDesc",
        path: "/app/settings/recruitment/form-templates",
        icon: FileText,
        permissions: SETTINGS_PERMISSIONS.recruitmentFormTemplates,
      },
      {
        key: "recruitmentAutomationRules",
        labelKey: "settings.recruitmentAutomationRules",
        descriptionKey: "settings.recruitmentAutomationRulesDesc",
        path: "/app/settings/recruitment/automation-rules",
        icon: Workflow,
        permissions: SETTINGS_PERMISSIONS.recruitmentAutomationRules,
      },
    ],
  },
];
