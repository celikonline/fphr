import { CalendarRange } from "lucide-react";
import { useTranslation } from "react-i18next";
import { useState } from "react";
import { Tabs, Text, Title } from "@mantine/core";
import NoAccess from "@/components/no-access";
import { useAnyPermission } from "@/components/permission-guard";
import RotationAssignPanel from "./rotation-assign-panel";
import RotationPatternsPanel from "./rotation-patterns-panel";

/** Vardiya rotasyonları (Z3.3): kalıp tanımı (hazır 2×12 / 3×8 / 4×6) ve çalışanlara atama. */
export default function RotationsPage() {
  const { t } = useTranslation(["rotations"]);
  const canView = useAnyPermission(["attendance.rotation.view"]);
  const canManage = useAnyPermission(["attendance.rotation.manage"]);
  const [activeTab, setActiveTab] = useState<string | null>("patterns");
  if (!canView) return <NoAccess />;

  return (
    <div className="max-w-5xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <CalendarRange size={24} /> {t("rotations:title")}
        </Title>
        <Text c="dimmed" size="sm" mt={4}>
          {t("rotations:subtitle")}
        </Text>
      </div>
      <Tabs value={activeTab} onChange={setActiveTab} keepMounted={false}>
        <Tabs.List>
          <Tabs.Tab value="patterns">{t("rotations:patterns")}</Tabs.Tab>
          <Tabs.Tab value="assign">{t("rotations:assign")}</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="patterns" pt="md">
          <RotationPatternsPanel canManage={canManage} />
        </Tabs.Panel>
        <Tabs.Panel value="assign" pt="md">
          <RotationAssignPanel canManage={canManage} onCreatePattern={() => setActiveTab("patterns")} />
        </Tabs.Panel>
      </Tabs>
    </div>
  );
}
