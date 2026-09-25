import type { ComponentType } from "react";
import { Stack } from "@mantine/core";
import { useAuthStore } from "@/store/auth.store";
import type { AccessSettingKey } from "@/types";
import type { ToggleableModuleKey } from "@/config/modules";
import type { EmployeeDetail } from "@/services/employees.service";
import { ASSETS_CARD_TAB_PERMISSIONS, AssetsSection } from "./assets-section";
import { CareerSection } from "./career-section";
import { CustomFieldsSection } from "./custom-fields-section";
import { DOCUMENTS_CARD_TAB_PERMISSIONS, DocumentsSection } from "./documents-section";
import { AccountSection, GeneralSection, PersonalSection } from "./employee-card-sections";
import { EmployeeNotesCard } from "./employee-notes-card";
import { EmployeeOnboardingCard } from "./employee-onboarding-card";
import { OtherSection } from "./other-section";
import { PaymentsSection } from "./payments-section";
import { PAYROLL_CARD_TAB_PERMISSIONS, PayrollSection } from "./payroll-section";
import { SubcontractorSection } from "./subcontractor-section";
import { UnionSection } from "./union-section";
import { LEAVE_CARD_TAB_PERMISSIONS, LeaveSection } from "@/components/leave/leave-section";
import { OVERTIME_CARD_TAB_PERMISSION, OvertimeSection } from "@/components/leave/overtime-section";

export interface EmployeeCardTabProps {
  employee: EmployeeDetail;
}

export interface EmployeeCardTab {
  /** URL segment: /app/employees/:id/:tab */
  key: string;
  labelKey: string;
  /** Hidden unless the user has this permission (an array: any of them). */
  permission?: string | string[];
  /** Hidden unless this tenant access setting is effective for the user. */
  accessSetting?: AccessSettingKey;
  /** Hidden when this toggleable module is turned off for the tenant (Ayarlar → Modüller, senseik-057). */
  module?: ToggleableModuleKey;
  /** Roadmap phase badge ("Faz 1b") for a tab whose module is not built yet. */
  phase?: string;
  component: ComponentType<EmployeeCardTabProps>;
}

/** Genel bilgiler + "Alt işveren" (E5b). */
function GeneralWithSubcontractor({ employee }: EmployeeCardTabProps) {
  return (
    <Stack gap="lg">
      <GeneralSection employee={employee} />
      <SubcontractorSection employee={employee} />
    </Stack>
  );
}

/** Kişisel bilgiler + "Sendika bilgileri" (E5b); the union block hides itself without employee.union.view. */
function PersonalWithUnion({ employee }: EmployeeCardTabProps) {
  return (
    <Stack gap="lg">
      <PersonalSection employee={employee} />
      <UnionSection employee={employee} />
    </Stack>
  );
}

/**
 * Employee card tabs, in display order. To fill a slot (e.g. Leave/Overtime from PC-B's modules) replace the entry's
 * `component` with the real one - one array entry plus one component, nothing else in the card changes.
 */
export const EMPLOYEE_CARD_TABS: EmployeeCardTab[] = [
  { key: "general", labelKey: "employees:card.tabs.general", component: GeneralWithSubcontractor },
  { key: "personal", labelKey: "employees:card.tabs.personal", component: PersonalWithUnion },
  {
    key: "customFields",
    labelKey: "employees:card.tabs.customFields",
    component: CustomFieldsSection,
  },
  { key: "career", labelKey: "employees:card.tabs.career", component: CareerSection },
  {
    key: "payments",
    labelKey: "employees:card.tabs.payments",
    // Salary needs employee.salary.view, bank accounts employee.employees.read; the own card (employee.my.view) shows both.
    permission: ["employee.salary.view", "employee.employees.read", "employee.my.view"],
    component: PaymentsSection,
  },
  {
    key: "payroll",
    labelKey: "employees:card.tabs.payroll",
    permission: PAYROLL_CARD_TAB_PERMISSIONS,
    module: "payroll",
    component: PayrollSection,
  },
  {
    key: "documents",
    labelKey: "employees:card.tabs.documents",
    permission: DOCUMENTS_CARD_TAB_PERMISSIONS,
    component: DocumentsSection,
  },
  {
    key: "assets",
    labelKey: "employees:card.tabs.assets",
    permission: ASSETS_CARD_TAB_PERMISSIONS,
    module: "assets",
    component: AssetsSection,
  },
  {
    key: "leave",
    labelKey: "employees:card.tabs.leave",
    permission: LEAVE_CARD_TAB_PERMISSIONS,
    module: "leave",
    component: LeaveSection,
  },
  {
    key: "overtime",
    labelKey: "employees:card.tabs.overtime",
    permission: OVERTIME_CARD_TAB_PERMISSION,
    module: "leave",
    component: OvertimeSection,
  },
  { key: "other", labelKey: "employees:card.tabs.other", component: OtherSection },
  { key: "account", labelKey: "employees:card.tabs.account", component: AccountSection },
  {
    key: "onboarding",
    labelKey: "employees:card.tabs.onboarding",
    permission: "onboarding.tasks.view",
    module: "onboarding",
    component: ({ employee }) => <EmployeeOnboardingCard employeeId={employee.id} />,
  },
  {
    key: "notes",
    labelKey: "employees:card.tabs.notes",
    permission: "employee.notes.view",
    component: ({ employee }) => <EmployeeNotesCard employeeId={employee.id} />,
  },
];

/** Tabs the current user may see (permission / access setting filtered); never empty (Genel has no gate). */
export function useVisibleEmployeeCardTabs(): EmployeeCardTab[] {
  const hasPermission = useAuthStore((state) => state.hasPermission);
  const isAccessSettingEnabled = useAuthStore((state) => state.isAccessSettingEnabled);
  const isModuleEnabled = useAuthStore((state) => state.isModuleEnabled);

  const allowed = (permission: EmployeeCardTab["permission"]) =>
    !permission ||
    (Array.isArray(permission)
      ? permission.some((key) => hasPermission(key))
      : hasPermission(permission));

  return EMPLOYEE_CARD_TABS.filter(
    (tab) =>
      allowed(tab.permission) &&
      (!tab.accessSetting || isAccessSettingEnabled(tab.accessSetting)) &&
      (!tab.module || isModuleEnabled(tab.module))
  );
}
