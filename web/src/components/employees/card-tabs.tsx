import type { ComponentType } from "react";
import { useTranslation } from "react-i18next";
import { Badge, Card, Group, Stack, Text, Title } from "@mantine/core";
import { Hourglass } from "lucide-react";
import { useAuthStore } from "@/store/auth.store";
import type { AccessSettingKey } from "@/types";
import type { EmployeeDetail } from "@/services/employees.service";
import { CareerSection } from "./career-section";
import { CustomFieldsSection } from "./custom-fields-section";
import { DOCUMENTS_CARD_TAB_PERMISSIONS, DocumentsSection } from "./documents-section";
import { AccountSection, GeneralSection, PersonalSection } from "./employee-card-sections";
import { PaymentsSection } from "./payments-section";
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
  /** Roadmap phase badge ("Faz 1b") for a tab whose module is not built yet. */
  phase?: string;
  component: ComponentType<EmployeeCardTabProps>;
}

/** Placeholder for a tab whose content ships with a later package/module. */
function Placeholder({ titleKey, noteKey, phase }: { titleKey: string; noteKey: string; phase?: string }) {
  const { t } = useTranslation(["employees"]);
  return (
    <Card withBorder padding="lg">
      <Group gap="xs" mb="sm">
        <Hourglass size={16} color="var(--mantine-color-dimmed)" />
        <Title order={4}>{t(titleKey)}</Title>
        {phase && <Badge variant="light">{t("employees:phase", { phase })}</Badge>}
      </Group>
      <Text size="sm" c="dimmed">
        {t(noteKey)}
      </Text>
    </Card>
  );
}

function tabPlaceholder(key: string, phase?: string) {
  const Component = () => (
    <Placeholder titleKey={`employees:card.tabs.${key}`} noteKey={`employees:card.tabNotes.${key}`} phase={phase} />
  );
  Component.displayName = `EmployeeCardTab_${key}`;
  return Component;
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
  { key: "customFields", labelKey: "employees:card.tabs.customFields", component: CustomFieldsSection },
  { key: "career", labelKey: "employees:card.tabs.career", component: CareerSection },
  {
    key: "payments",
    labelKey: "employees:card.tabs.payments",
    // Salary needs employee.salary.view, bank accounts employee.employees.read; the own card (employee.my.view) shows both.
    permission: ["employee.salary.view", "employee.employees.read", "employee.my.view"],
    component: PaymentsSection,
  },
  { key: "documents", labelKey: "employees:card.tabs.documents", permission: DOCUMENTS_CARD_TAB_PERMISSIONS, component: DocumentsSection },
  { key: "leave", labelKey: "employees:card.tabs.leave", permission: LEAVE_CARD_TAB_PERMISSIONS, component: LeaveSection },
  { key: "overtime", labelKey: "employees:card.tabs.overtime", permission: OVERTIME_CARD_TAB_PERMISSION, component: OvertimeSection },
  { key: "other", labelKey: "employees:card.tabs.other", component: tabPlaceholder("other") },
  { key: "account", labelKey: "employees:card.tabs.account", component: AccountSection },
];

/** Tabs the current user may see (permission / access setting filtered); never empty (Genel has no gate). */
export function useVisibleEmployeeCardTabs(): EmployeeCardTab[] {
  const hasPermission = useAuthStore((state) => state.hasPermission);
  const isAccessSettingEnabled = useAuthStore((state) => state.isAccessSettingEnabled);

  const allowed = (permission: EmployeeCardTab["permission"]) =>
    !permission || (Array.isArray(permission) ? permission.some((key) => hasPermission(key)) : hasPermission(permission));

  return EMPLOYEE_CARD_TABS.filter((tab) => allowed(tab.permission) && (!tab.accessSetting || isAccessSettingEnabled(tab.accessSetting)));
}
