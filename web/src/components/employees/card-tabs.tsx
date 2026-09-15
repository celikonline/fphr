import type { ComponentType } from "react";
import { useTranslation } from "react-i18next";
import { Hourglass } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { useAuthStore } from "@/store/auth.store";
import type { AccessSettingKey } from "@/types";
import type { EmployeeDetail } from "@/services/employees.service";
import { AccountSection, CareerSection, GeneralSection, PersonalSection } from "./employee-card-sections";

export interface EmployeeCardTabProps {
  employee: EmployeeDetail;
}

export interface EmployeeCardTab {
  /** URL segment: /app/employees/:id/:tab */
  key: string;
  labelKey: string;
  /** Hidden unless the user has this permission. */
  permission?: string;
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
    <Card>
      <CardHeader className="flex flex-row items-center gap-2 space-y-0 pb-3">
        <Hourglass className="h-4 w-4 text-muted-foreground" />
        <CardTitle className="text-lg">{t(titleKey)}</CardTitle>
        {phase && <Badge variant="secondary">{t("employees:phase", { phase })}</Badge>}
      </CardHeader>
      <CardContent>
        <p className="text-sm text-muted-foreground">{t(noteKey)}</p>
      </CardContent>
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

/**
 * Employee card tabs, in display order. To fill a slot (e.g. Leave/Overtime from PC-B's modules) replace the entry's
 * `component` with the real one - one array entry plus one component, nothing else in the card changes.
 */
export const EMPLOYEE_CARD_TABS: EmployeeCardTab[] = [
  { key: "general", labelKey: "employees:card.tabs.general", component: GeneralSection },
  { key: "personal", labelKey: "employees:card.tabs.personal", component: PersonalSection },
  { key: "career", labelKey: "employees:card.tabs.career", component: CareerSection },
  {
    key: "payments",
    labelKey: "employees:card.tabs.payments",
    permission: "employee.salary.view",
    phase: "1a",
    component: tabPlaceholder("payments", "1a"),
  },
  { key: "leave", labelKey: "employees:card.tabs.leave", phase: "1b", component: tabPlaceholder("leave", "1b") },
  { key: "overtime", labelKey: "employees:card.tabs.overtime", phase: "1b", component: tabPlaceholder("overtime", "1b") },
  { key: "other", labelKey: "employees:card.tabs.other", component: tabPlaceholder("other") },
  { key: "account", labelKey: "employees:card.tabs.account", component: AccountSection },
];

/** Tabs the current user may see (permission / access setting filtered); never empty (Genel has no gate). */
export function useVisibleEmployeeCardTabs(): EmployeeCardTab[] {
  const hasPermission = useAuthStore((state) => state.hasPermission);
  const isAccessSettingEnabled = useAuthStore((state) => state.isAccessSettingEnabled);

  return EMPLOYEE_CARD_TABS.filter(
    (tab) =>
      (!tab.permission || hasPermission(tab.permission)) && (!tab.accessSetting || isAccessSettingEnabled(tab.accessSetting)),
  );
}
