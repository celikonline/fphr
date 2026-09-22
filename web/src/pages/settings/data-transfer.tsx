import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Card, SimpleGrid, Skeleton, Stack, Tabs, Text, Title } from "@mantine/core";
import { ArrowLeftRight } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import ImportHistory from "@/components/data-transfer/import-history";
import ImportRules from "@/components/data-transfer/import-rules";
import TemplateCard from "@/components/data-transfer/template-card";
import { useApiLanguage } from "@/components/data-transfer/use-api-language";
import { DataTransferPermissions, listImportTemplates } from "@/services/data-transfer.service";

const TABS = ["import", "history"] as const;
type Tab = (typeof TABS)[number];

export default function DataTransferPage() {
  const canView = usePermission(DataTransferPermissions.View);
  if (!canView) return <NoAccess />;
  return <DataTransferCenter />;
}

function DataTransferCenter() {
  const { t } = useTranslation(["dataTransfer", "common"]);
  const lang = useApiLanguage();
  const [searchParams, setSearchParams] = useSearchParams();
  const tabParam = searchParams.get("tab");
  const tab: Tab = TABS.includes(tabParam as Tab) ? (tabParam as Tab) : "import";

  const templatesQuery = useQuery({
    queryKey: ["datatransfer", "templates", lang],
    queryFn: () => listImportTemplates(lang),
  });

  function onTabChange(value: string | null) {
    if (!value) return;
    setSearchParams(value === "import" ? {} : { tab: value }, { replace: true });
  }

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={2} className="flex items-center gap-2">
          <ArrowLeftRight size={24} /> {t("dataTransfer:title")}
        </Title>
        <Text c="dimmed">{t("dataTransfer:subtitle")}</Text>
      </div>

      <Tabs value={tab} onChange={onTabChange}>
        <Tabs.List>
          <Tabs.Tab value="import">{t("dataTransfer:tabs.import")}</Tabs.Tab>
          <Tabs.Tab value="history">{t("dataTransfer:tabs.history")}</Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="import" pt="md">
          <Stack gap="lg">
            <ImportRules />
            {templatesQuery.isLoading ? (
              <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
                <Skeleton h={256} />
                <Skeleton h={256} />
              </SimpleGrid>
            ) : templatesQuery.isError ? (
              <Card withBorder padding="lg">
                <Text size="sm" c="red" ta="center">
                  {t("dataTransfer:templates.loadError")}
                </Text>
              </Card>
            ) : !templatesQuery.data || templatesQuery.data.length === 0 ? (
              <Card withBorder padding="lg">
                <Text size="sm" c="dimmed" ta="center">
                  {t("dataTransfer:templates.empty")}
                </Text>
              </Card>
            ) : (
              <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
                {templatesQuery.data.map((template) => (
                  <TemplateCard key={template.code} template={template} />
                ))}
              </SimpleGrid>
            )}
          </Stack>
        </Tabs.Panel>

        <Tabs.Panel value="history" pt="md">
          <ImportHistory templates={templatesQuery.data} />
        </Tabs.Panel>
      </Tabs>
    </div>
  );
}
