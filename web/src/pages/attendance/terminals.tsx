import { useTranslation } from "react-i18next";
import { Tabs, Text, Title } from "@mantine/core";
import { QrCode } from "lucide-react";
import NoAccess from "@/components/no-access";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import TerminalIdentitiesTab from "@/components/attendance/terminals/identities-tab";
import TerminalGroupsTab from "@/components/attendance/terminals/groups-tab";
import TerminalsTab from "@/components/attendance/terminals/terminals-tab";

/** PDKS terminal yönetimi: terminaller, gruplar, çalışan kimlikleri (attendance.terminal.view/manage). */
export default function TerminalsPage() {
  const { t } = useTranslation(["terminals"]);
  const canView = useAnyPermission(["attendance.terminal.view", "attendance.terminal.manage"]);
  const canManage = usePermission("attendance.terminal.manage");
  if (!canView) return <NoAccess />;

  return (
    <div className="max-w-5xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <QrCode size={24} /> {t("terminals:title")}
        </Title>
        <Text c="dimmed" size="sm" mt={4}>
          {t("terminals:subtitle")}
        </Text>
      </div>
      <Tabs defaultValue="terminals" keepMounted={false}>
        <Tabs.List>
          <Tabs.Tab value="terminals">{t("terminals:tabs.terminals")}</Tabs.Tab>
          <Tabs.Tab value="groups">{t("terminals:tabs.groups")}</Tabs.Tab>
          <Tabs.Tab value="credentials">{t("terminals:tabs.credentials")}</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="terminals" pt="md">
          <TerminalsTab canManage={canManage} />
        </Tabs.Panel>
        <Tabs.Panel value="groups" pt="md">
          <TerminalGroupsTab canManage={canManage} />
        </Tabs.Panel>
        <Tabs.Panel value="credentials" pt="md">
          <TerminalIdentitiesTab canManage={canManage} />
        </Tabs.Panel>
      </Tabs>
    </div>
  );
}
