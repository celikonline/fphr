import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeftRight } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
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

  function onTabChange(value: string) {
    setSearchParams(value === "import" ? {} : { tab: value }, { replace: true });
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          <ArrowLeftRight className="h-6 w-6" /> {t("dataTransfer:title")}
        </h1>
        <p className="text-muted-foreground">{t("dataTransfer:subtitle")}</p>
      </div>

      <Tabs value={tab} onValueChange={onTabChange}>
        <TabsList>
          <TabsTrigger value="import">{t("dataTransfer:tabs.import")}</TabsTrigger>
          <TabsTrigger value="history">{t("dataTransfer:tabs.history")}</TabsTrigger>
        </TabsList>

        <TabsContent value="import" className="space-y-6 pt-4">
          <ImportRules />
          {templatesQuery.isLoading ? (
            <div className="grid gap-4 md:grid-cols-2">
              <Skeleton className="h-64 w-full" />
              <Skeleton className="h-64 w-full" />
            </div>
          ) : templatesQuery.isError ? (
            <Card>
              <CardContent className="p-6 text-center text-sm text-destructive">
                {t("dataTransfer:templates.loadError")}
              </CardContent>
            </Card>
          ) : !templatesQuery.data || templatesQuery.data.length === 0 ? (
            <Card>
              <CardContent className="p-6 text-center text-sm text-muted-foreground">
                {t("dataTransfer:templates.empty")}
              </CardContent>
            </Card>
          ) : (
            <div className="grid gap-4 md:grid-cols-2">
              {templatesQuery.data.map((template) => (
                <TemplateCard key={template.code} template={template} />
              ))}
            </div>
          )}
        </TabsContent>

        <TabsContent value="history" className="pt-4">
          <ImportHistory templates={templatesQuery.data} />
        </TabsContent>
      </Tabs>
    </div>
  );
}
