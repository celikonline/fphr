import type { LucideIcon } from "lucide-react";
import {
  AppWindow,
  BarChart3,
  Briefcase,
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
  Lock,
  LogOut,
  MapPin,
  Network,
  NotebookText,
  Package,
  Palmtree,
  PieChart,
  Receipt,
  ScrollText,
  Settings,
  ShieldCheck,
  Sparkles,
  Target,
  Timer,
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
  FileUp,
  FolderOpen,
  HardHat,
  ClipboardCheck,
  Blocks,
  QrCode,
  MonitorCheck,
  Mail,
  Megaphone,
  GraduationCap,
  Smartphone,
  Calculator,
  BadgeCheck,
  LayoutDashboard,
} from "lucide-react";
import type { AccessSettingKey } from "@/types";

/** Corporate palette keys from mantine-theme.ts usable for per-item icon accents. */
export type CorporateColorKey =
  "brand" | "navy" | "blue" | "cyan" | "teal" | "orange" | "gold" | "steel";

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

/** CSS var for a NavItem's (or NavGroup's) icon accent, for use as the lucide icon's `color` prop or a styles override. */
export function navIconColor(item: { iconColor?: CorporateColorKey }): string {
  return item.iconColor
    ? `light-dark(var(--mantine-color-${item.iconColor}-6), var(--mantine-color-${item.iconColor}-4))`
    : "var(--mantine-primary-color-filled)";
}

/** Settings hub card permissions, reused so the "Ayarlar" menu entry shows whenever any card would. */
export const SETTINGS_PERMISSIONS = {
  structure: ["organization.units.read"],
  holidayCalendars: ["organization.holiday_calendars.read"],
  locations: ["organization.locations.read"],
  companyProfile: ["identity.tenant.view", "identity.tenant.manage"],
  tenantManage: ["identity.tenant.manage"],
  access: ["identity.tenant.view", "identity.tenant.manage"],
  securityPolicy: ["identity.tenant.view", "identity.security.manage"],
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
  expenseCategories: ["expense.categories.view", "expense.categories.manage"],
  recruitmentFormTemplates: ["recruitment.form_templates.read"],
  recruitmentAutomationRules: ["recruitment.automation_rules.read"],
  workSchedules: ["attendance.workschedule.view"],
  // senseik-001: vize belge süreçleri (approver, template documents, process definitions).
  visaProcesses: ["documents.visa.manage"],
  ohsHazardClassification: ["ohs.hazard_classifications.view", "ohs.hazard_classifications.manage"],
  ohsRiskAssessments: ["ohs.risk_assessments.view", "ohs.risk_assessments.manage"],
  // senseik-057: read = identity.tenant.view (same as Erişim ayarları), change = identity.modules.manage.
  modules: ["identity.tenant.view", "identity.modules.manage"],
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
    // senseik-048: "İzinlerim"/"Fazla mesailerim" YANINDA şirket geneli görünüm (İK Müdürü/Hesap Sahibi) - ayrı bir
    // izinle (leave.company_ledger.read) kapılı, HR/HRSpecialist "leave.*" ve Owner/Admin "*" joker desenleriyle
    // otomatik alır (SystemRoleDefinitions'a append edilmedi, doğrulandı - bkz. LeavePermissions.CompanyLedgerRead).
    key: "companyLedger",
    labelKey: "nav.companyLedger",
    path: "/app/leave/company-ledger",
    icon: NotebookText,
    permissions: ["leave.company_ledger.read"],
    iconColor: "teal",
  },
  {
    key: "leaveRules",
    labelKey: "nav.leaveRules",
    path: "/app/settings/leave/rule-sets",
    icon: ScrollText,
    permissions: ["leave.rule_sets.read"],
    iconColor: "steel",
  },
  {
    key: "requests",
    labelKey: "nav.requests",
    path: "/app/requests",
    icon: ClipboardList,
    iconColor: "gold",
  },
  { key: "apps", labelKey: "nav.apps", path: "/app/apps", icon: AppWindow, iconColor: "navy" },
  {
    key: "reports",
    labelKey: "nav.reports",
    path: "/app/reports",
    icon: BarChart3,
    permissions: ["analytics.kpi.view", "reports.catalog.view", "reports.my.executions", "reports.custom.build"],
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
  // senseik-028: appended at the end (hot file, append-only) rather than placed near "documents"-flavored items above.
  {
    key: "sharedFiles",
    labelKey: "nav.sharedFiles",
    path: "/app/shared-files",
    icon: FolderOpen,
    permissions: ["documents.company.view"],
    iconColor: "gold",
  },
  // Süper admin (platform kiracısı): yeni şirket oluşturma/listeleme. Yalnız `platform.tenants.manage` sahibi görür ve
  // ROUTE_ACCESS bu girdiden türediği için URL ile de erişilemez.
  {
    key: "platformTenants",
    labelKey: "nav.platformTenants",
    path: "/app/platform/tenants",
    icon: Building2,
    permissions: ["platform.tenants.manage"],
    iconColor: "brand",
  },
  {
    key: "platformMaintenance",
    labelKey: "nav.platformMaintenance",
    path: "/app/platform/maintenance",
    icon: HardHat,
    permissions: ["platform.operations.view"],
    iconColor: "orange",
  },
];

export const NAV_FOOTER_ITEMS: NavItem[] = [
  {
    key: "support",
    labelKey: "nav.support",
    path: "/app/support",
    icon: LifeBuoy,
    iconColor: "orange",
  },
  {
    key: "mobileApp",
    labelKey: "nav.mobileApp",
    path: "/app/mobile-app",
    icon: Smartphone,
    iconColor: "teal",
  },
  {
    key: "account",
    labelKey: "nav.account",
    path: "/app/account",
    icon: UserCircle,
    iconColor: "navy",
  },
];

/** "Uygulamalar" launcher entries (header dropdown + /app/apps): related apps grouped, groups and items ordered by how often they are used (daily general/attendance first, admin tools last). Entries with a phase are shown disabled. */
export const APP_LAUNCHER_ITEMS: NavItem[] = [
  {
    key: "pdksCenter",
    labelKey: "apps.pdksCenter",
    path: "/app/attendance/management",
    icon: LayoutDashboard,
    permissions: [
      "attendance.my.timesheet",
      "attendance.my.checkin",
      "attendance.shift.self",
      "attendance.timesheet.view",
      "attendance.shift.view",
      "attendance.board.view",
      "attendance.timesheet.import",
      "attendance.daily.view",
      "attendance.dashboard.view",
      "attendance.shiftdefinition.view",
      "attendance.rotation.view",
      "attendance.punchrequest.manage",
      "attendance.my.punchrequest",
      "attendance.terminal.view",
      "attendance.terminal.manage",
    ],
    iconColor: "orange",
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
    key: "announcements",
    labelKey: "apps.announcements",
    path: "/app/announcements",
    icon: Megaphone,
    permissions: ["announcements.my.feed", "announcements.announcements.manage"],
    iconColor: "orange",
  },
  {
    key: "companyCalendar",
    labelKey: "apps.companyCalendar",
    path: "/app/company-calendar",
    icon: CalendarDays,
    permissions: ["announcements.my.events"],
    iconColor: "cyan",
  },
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
    key: "timesheet",
    labelKey: "apps.timesheet",
    path: "/app/attendance/timesheet",
    icon: CalendarClock,
    permissions: ["attendance.my.timesheet"],
    iconColor: "orange",
  },
  {
    key: "selfCheckIn",
    labelKey: "apps.selfCheckIn",
    path: "/app/attendance/self-check-in",
    icon: QrCode,
    permissions: ["attendance.my.checkin"],
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
    key: "monthlyTimesheet",
    labelKey: "apps.monthlyTimesheet",
    path: "/app/attendance/monthly-timesheet",
    icon: ClipboardCheck,
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
    key: "liveBoard",
    labelKey: "apps.liveBoard",
    path: "/app/attendance/live-board",
    icon: MonitorCheck,
    permissions: ["attendance.board.view"],
    iconColor: "navy",
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
  {
    key: "payrollCenter",
    labelKey: "apps.payrollCenter",
    path: "/app/payroll/management",
    icon: Calculator,
    permissions: ["payroll.period.recalculate", "payroll.profile.manage", "payroll.payitem.manage", "payroll.incentive.manage", "payroll.payslip.import"],
    iconColor: "navy",
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
    key: "training",
    labelKey: "apps.training",
    path: "/app/training",
    icon: GraduationCap,
    permissions: ["training.my.enrollments", "training.programs.view"],
    iconColor: "teal",
  },
  {
    key: "assets",
    labelKey: "apps.assets",
    path: "/app/assets",
    icon: Package,
    permissions: ["assets.my.assets", "assets.assets.view"],
    iconColor: "steel",
  },
  {
    key: "ohs",
    labelKey: "apps.ohs",
    path: "/app/ohs",
    icon: HardHat,
    permissions: ["ohs.my.incident_reports", "ohs.incidents.view", "ohs.services.view"],
    iconColor: "orange",
  },
  {
    key: "onboarding",
    labelKey: "apps.onboarding",
    path: "/app/onboarding",
    icon: ListChecks,
    permissions: ["onboarding.tasks.view"],
    iconColor: "teal",
  },
  {
    key: "performance",
    labelKey: "apps.performance",
    path: "/app/performance/goals",
    icon: Target,
    permissions: ["performance.my.goals"],
    iconColor: "gold",
  },
  {
    key: "performanceReviewCycles",
    labelKey: "apps.performanceReviewCycles",
    path: "/app/performance/review-cycles",
    icon: CalendarRange,
    permissions: ["performance.my.reviews"],
    iconColor: "gold",
  },
  {
    key: "performanceTeamGoals",
    labelKey: "apps.performanceTeamGoals",
    path: "/app/performance/team-goals",
    icon: Users,
    permissions: ["performance.team.goals"],
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
    permissions: ["analytics.kpi.view", "reports.catalog.view", "reports.my.executions", "reports.custom.build"],
    iconColor: "blue",
  },
  {
    key: "dataTransfer",
    labelKey: "apps.dataTransfer",
    path: "/app/settings/data-transfer",
    icon: FileSpreadsheet,
    permissions: [...SETTINGS_PERMISSIONS.dataTransfer],
    iconColor: "blue",
  },
  // Zaman-devam iskeleti (W0-c): izin anahtarları Attendance.Contracts'ta tanımlı (W0-b).
  {
    key: "dailyList",
    labelKey: "apps.dailyList",
    path: "/app/attendance/daily/arrived",
    icon: ClipboardList,
    permissions: ["attendance.daily.view"],
    iconColor: "navy",
  },
  {
    key: "timeDashboard",
    labelKey: "apps.timeDashboard",
    path: "/app/attendance/time-dashboard",
    icon: PieChart,
    permissions: ["attendance.dashboard.view"],
    iconColor: "navy",
  },
  {
    key: "shiftDefinitions",
    labelKey: "apps.shiftDefinitions",
    path: "/app/attendance/shift-definitions",
    icon: Clock,
    permissions: ["attendance.shiftdefinition.view"],
    iconColor: "teal",
  },
  {
    key: "rotations",
    labelKey: "apps.rotations",
    path: "/app/attendance/rotations",
    icon: CalendarRange,
    permissions: ["attendance.rotation.view"],
    iconColor: "teal",
  },
  {
    key: "monthlyMatrix",
    labelKey: "apps.monthlyMatrix",
    path: "/app/attendance/monthly-matrix",
    icon: FileSpreadsheet,
    permissions: ["attendance.daily.view"],
    iconColor: "navy",
  },
  {
    key: "punchRequests",
    labelKey: "apps.punchRequests",
    path: "/app/attendance/punch-requests",
    icon: Stamp,
    permissions: ["attendance.punchrequest.manage", "attendance.my.punchrequest"],
    iconColor: "orange",
  },
  {
    key: "terminals",
    labelKey: "apps.terminals",
    path: "/app/attendance/terminals",
    icon: QrCode,
    permissions: ["attendance.terminal.view"],
    iconColor: "orange",
  },
  {
    key: "payslipImport",
    labelKey: "apps.payslipImport",
    path: "/app/payroll/payslip-import",
    icon: FileUp,
    permissions: ["payroll.payslip.import"],
    iconColor: "navy",
  },
  {
    key: "messages",
    labelKey: "apps.messages",
    path: "/app/messages",
    icon: Mail,
    permissions: ["notifications.messages.view"],
    iconColor: "cyan",
  },
  {
    key: "payItems",
    labelKey: "apps.payItems",
    path: "/app/payroll/pay-items",
    icon: Calculator,
    permissions: ["payroll.payitem.manage"],
    iconColor: "navy",
  },
  {
    key: "incentives",
    labelKey: "apps.incentives",
    path: "/app/payroll/incentives",
    icon: BadgeCheck,
    permissions: ["payroll.incentive.manage"],
    iconColor: "navy",
  },
];

/** App hub groups. Keeps the full module catalog readable while reusing the existing navigation labels. */
export const APP_LAUNCHER_SECTIONS = [
  {
    key: "humanResources",
    labelKey: "navGroups.humanResources",
    itemKeys: ["orgChart", "onboarding", "assets", "ohs"],
  },
  {
    key: "timeManagement",
    labelKey: "navGroups.timeManagement",
    itemKeys: [
      "pdksCenter",
      "timesheet",
      "selfCheckIn",
      "shifts",
      "teamTimesheet",
      "monthlyTimesheet",
      "shiftPlan",
      "liveBoard",
      "deviceLogImport",
      "dailyList",
      "timeDashboard",
      "shiftDefinitions",
      "rotations",
      "monthlyMatrix",
      "punchRequests",
      "terminals",
    ],
  },
  { key: "leave", labelKey: "navGroups.leave", itemKeys: ["overtime"] },
  {
    key: "expensePayroll",
    labelKey: "navGroups.expensePayroll",
    itemKeys: [
      "expenses",
      "expensePaymentsAdmin",
      "payrollCenter",
      "payroll",
      "payslipImport",
      "payItems",
      "incentives",
    ],
  },
  {
    key: "performance",
    labelKey: "navGroups.performance",
    itemKeys: ["performance", "performanceReviewCycles", "performanceTeamGoals"],
  },
  {
    key: "recruitment",
    labelKey: "navGroups.recruitment",
    itemKeys: ["recruitment", "recruitmentCandidates", "recruitmentRequisitions"],
  },
  {
    key: "employeeExperience",
    labelKey: "navGroups.employeeExperience",
    itemKeys: ["workflowInbox", "announcements", "companyCalendar", "training", "messages"],
  },
  { key: "insights", labelKey: "navGroups.insights", itemKeys: ["analytics", "dataTransfer"] },
] as const;

export interface SettingsLink {
  key: string;
  /** i18n keys in the "navigation" namespace. */
  labelKey: string;
  descriptionKey: string;
  path: string;
  icon: LucideIcon;
  permissions?: readonly string[];
  /** Rota bir erişim ayarıyla da kapılıysa (route-guard aynı ayarı okur). */
  accessSetting?: AccessSettingKey;
  phase?: number;
}

export interface SettingsSection {
  key: string;
  labelKey: string;
  path?: string;
  links: SettingsLink[];
}

/** Ayarlar hub tree (11.3.13 "Ayarlar ağacı"): işlevsel yakınlığa göre gruplanmıştır; arama hub sayfasında yapılır. */
export const SETTINGS_SECTIONS: SettingsSection[] = [
  {
    key: "company",
    labelKey: "settings.company",
    links: [
      {
        key: "companyProfile",
        labelKey: "settings.companyProfile",
        descriptionKey: "settings.companyProfileDesc",
        path: "/app/settings/company/profile",
        icon: Building2,
        permissions: SETTINGS_PERMISSIONS.companyProfile,
      },
      {
        key: "structure",
        labelKey: "settings.structure",
        descriptionKey: "settings.structureDesc",
        path: "/app/settings/company/structure",
        icon: Network,
        permissions: SETTINGS_PERMISSIONS.structure,
      },
      {
        key: "orgChart",
        labelKey: "settings.orgChart",
        descriptionKey: "settings.orgChartDesc",
        path: "/app/employees/org-chart",
        icon: GitBranch,
        permissions: ["employee.orgchart.view"],
        accessSetting: "showOrgChart",
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
        key: "costCenters",
        labelKey: "settings.costCenters",
        descriptionKey: "settings.costCentersDesc",
        path: "/app/settings/company/cost-centers",
        icon: Landmark,
        permissions: SETTINGS_PERMISSIONS.costCenters,
      },
      {
        key: "personnelNumbers",
        labelKey: "settings.personnelNumbers",
        descriptionKey: "settings.personnelNumbersDesc",
        path: "/app/settings/company/personnel-numbers",
        icon: Landmark,
        permissions: ["employee.personnel_number.manage"],
      },
      {
        key: "subcontractors",
        labelKey: "settings.subcontractors",
        descriptionKey: "settings.subcontractorsDesc",
        path: "/app/settings/company/subcontractors",
        icon: Building2,
        permissions: SETTINGS_PERMISSIONS.subcontractors,
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
        key: "leaveRuleSets",
        labelKey: "settings.leaveRuleSets",
        descriptionKey: "settings.leaveRuleSetsDesc",
        path: "/app/settings/leave/rule-sets",
        icon: ScrollText,
        permissions: ["leave.rule_sets.read"],
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
        key: "visaProcesses",
        labelKey: "settings.visaProcesses",
        descriptionKey: "settings.visaProcessesDesc",
        path: "/app/settings/rules/visa-processes",
        icon: Stamp,
        permissions: SETTINGS_PERMISSIONS.visaProcesses,
      },
    ],
  },
  {
    key: "attendance",
    labelKey: "settings.attendanceSection",
    links: [
      {
        key: "pdksIntegrations",
        labelKey: "settings.pdksIntegrations",
        descriptionKey: "settings.pdksIntegrationsDesc",
        path: "/app/settings/pdks-integrations",
        icon: QrCode,
        permissions: ["attendance.terminal.view", "attendance.terminal.manage"],
      },
    ],
  },
  {
    key: "people",
    labelKey: "settings.peopleSection",
    links: [
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
    ],
  },
  {
    key: "workflows",
    labelKey: "settings.workflowsSection",
    links: [
      {
        key: "approvalFlows",
        labelKey: "settings.approvalFlows",
        descriptionKey: "settings.approvalFlowsDesc",
        path: "/app/settings/company/approval-flows",
        icon: Workflow,
        permissions: SETTINGS_PERMISSIONS.workflowPolicies,
      },
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
      {
        key: "delegations",
        labelKey: "settings.delegations",
        descriptionKey: "settings.delegationsDesc",
        path: "/app/settings/delegations",
        icon: Workflow,
        permissions: ["workflow.delegation.manage", "workflow.my.delegation"],
      },
    ],
  },
  {
    key: "assetsExpense",
    labelKey: "settings.assetsExpenseSection",
    links: [
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
    key: "ohs",
    labelKey: "settings.ohsSection",
    links: [
      {
        key: "ohsHazardClassification",
        labelKey: "settings.ohsHazardClassification",
        descriptionKey: "settings.ohsHazardClassificationDesc",
        path: "/app/settings/company/ohs-hazard-classification",
        icon: HardHat,
        permissions: SETTINGS_PERMISSIONS.ohsHazardClassification,
      },
      {
        key: "ohsRiskAssessments",
        labelKey: "settings.ohsRiskAssessments",
        descriptionKey: "settings.ohsRiskAssessmentsDesc",
        path: "/app/settings/company/ohs-risk-assessments",
        icon: ClipboardCheck,
        permissions: SETTINGS_PERMISSIONS.ohsRiskAssessments,
      },
    ],
  },
  {
    key: "system",
    labelKey: "settings.system",
    links: [
      {
        key: "usersRoles",
        labelKey: "settings.usersRoles",
        descriptionKey: "settings.usersRolesDesc",
        path: "/app/settings/system/users-roles",
        icon: Users,
        permissions: SETTINGS_PERMISSIONS.usersRoles,
      },
      {
        key: "access",
        labelKey: "settings.access",
        descriptionKey: "settings.accessDesc",
        path: "/app/settings/system/access",
        icon: KeyRound,
        permissions: SETTINGS_PERMISSIONS.access,
      },
      {
        key: "securityPolicy",
        labelKey: "settings.securityPolicy",
        descriptionKey: "settings.securityPolicyDesc",
        path: "/app/settings/system/security-policy",
        icon: Lock,
        permissions: SETTINGS_PERMISSIONS.securityPolicy,
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
    ],
  },
  {
    key: "platform",
    labelKey: "settings.platformSection",
    links: [
      {
        key: "modules",
        labelKey: "settings.modules",
        descriptionKey: "settings.modulesDesc",
        path: "/app/settings/system/modules",
        icon: Blocks,
        permissions: SETTINGS_PERMISSIONS.modules,
      },
      {
        key: "dataTransfer",
        labelKey: "settings.dataTransfer",
        descriptionKey: "settings.dataTransferDesc",
        path: "/app/settings/data-transfer",
        icon: FileSpreadsheet,
        permissions: SETTINGS_PERMISSIONS.dataTransfer,
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
];

/** A collapsible group of related NAV_ITEMS/APP_LAUNCHER_ITEMS entries in the grouped sidebar (11.10). */
export interface NavGroup {
  key: string;
  /** i18n key in the "navigation" namespace. */
  labelKey: string;
  icon: LucideIcon;
  iconColor?: CorporateColorKey;
  items: NavItem[];
}

/** One entry of the grouped sidebar: either a standalone NavItem or a NavGroup with its own sub-items. */
export type NavEntry = NavItem | NavGroup;

export function isNavGroup(entry: NavEntry): entry is NavGroup {
  return "items" in entry;
}

function requireNavItem(key: string): NavItem {
  const item = NAV_ITEMS.find((candidate) => candidate.key === key);
  if (!item) throw new Error(`NAV_SECTIONS: "${key}" not found in NAV_ITEMS`);
  return item;
}

function requireAppItem(key: string): NavItem {
  const item = APP_LAUNCHER_ITEMS.find((candidate) => candidate.key === key);
  if (!item) throw new Error(`NAV_SECTIONS: "${key}" not found in APP_LAUNCHER_ITEMS`);
  return item;
}

/**
 * Grouped, collapsible left sidebar (11.10): sections are separated by a divider in the UI; most sections hold a
 * single NavGroup, a few hold standalone NavItems. Reuses NavItem objects from NAV_ITEMS/APP_LAUNCHER_ITEMS by key
 * (never copies them) so permission/accessSetting/phase metadata stays a single source of truth.
 */
export const NAV_SECTIONS: NavEntry[][] = [
  [requireNavItem("home"), requireNavItem("workflowInbox"), requireNavItem("requests")],
  [
    {
      key: "humanResources",
      labelKey: "navGroups.humanResources",
      icon: Briefcase,
      iconColor: "blue",
      items: [
        requireNavItem("employees"),
        requireAppItem("orgChart"),
        requireNavItem("sharedFiles"),
        requireAppItem("onboarding"),
        requireAppItem("assets"),
        requireAppItem("ohs"),
      ],
    },
  ],
  [
    {
      key: "timeManagement",
      labelKey: "navGroups.timeManagement",
      icon: Timer,
      iconColor: "orange",
      items: [
        requireAppItem("pdksCenter"),
        requireAppItem("timesheet"),
        requireAppItem("selfCheckIn"),
        requireAppItem("shifts"),
        requireAppItem("teamTimesheet"),
        requireAppItem("monthlyTimesheet"),
        requireAppItem("shiftPlan"),
        requireAppItem("liveBoard"),
        requireAppItem("deviceLogImport"),
        requireAppItem("dailyList"),
        requireAppItem("timeDashboard"),
        requireAppItem("shiftDefinitions"),
        requireAppItem("rotations"),
        requireAppItem("monthlyMatrix"),
        requireAppItem("punchRequests"),
        requireAppItem("terminals"),
      ],
    },
  ],
  [
    {
      key: "leave",
      labelKey: "navGroups.leave",
      icon: Palmtree,
      iconColor: "teal",
      items: [
        requireNavItem("leaves"),
        requireNavItem("calendar"),
        requireNavItem("overtimes"),
        requireNavItem("companyLedger"),
        requireNavItem("leaveRules"),
      ],
    },
  ],
  [
    {
      key: "expensePayroll",
      labelKey: "navGroups.expensePayroll",
      icon: Wallet,
      iconColor: "navy",
      items: [
        requireAppItem("expenses"),
        requireAppItem("expensePaymentsAdmin"),
        requireAppItem("payrollCenter"),
        requireAppItem("payroll"),
        requireAppItem("payslipImport"),
        requireAppItem("payItems"),
        requireAppItem("incentives"),
      ],
    },
  ],
  [
    {
      key: "performance",
      labelKey: "navGroups.performance",
      icon: Target,
      iconColor: "gold",
      items: [
        requireAppItem("performance"),
        requireAppItem("performanceReviewCycles"),
        requireAppItem("performanceTeamGoals"),
      ],
    },
  ],
  [
    {
      key: "recruitment",
      labelKey: "navGroups.recruitment",
      icon: UserPlus,
      iconColor: "gold",
      items: [
        requireAppItem("recruitment"),
        requireAppItem("recruitmentCandidates"),
        requireAppItem("recruitmentRequisitions"),
      ],
    },
  ],
  [
    {
      key: "employeeExperience",
      labelKey: "navGroups.employeeExperience",
      icon: Sparkles,
      iconColor: "cyan",
      items: [
        requireAppItem("announcements"),
        requireAppItem("companyCalendar"),
        requireAppItem("training"),
        requireAppItem("messages"),
      ],
    },
  ],
  [requireNavItem("reports"), requireNavItem("apps")],
  [requireNavItem("settings"), requireNavItem("platformTenants"), requireNavItem("platformMaintenance")],
];
