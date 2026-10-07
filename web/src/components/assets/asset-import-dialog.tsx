import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Alert, Button, Loader, Modal, Stack, Text } from "@mantine/core";
import { AlertTriangle } from "lucide-react";
import TemplateCard from "@/components/data-transfer/template-card";
import { useApiLanguage } from "@/components/data-transfer/use-api-language";
import { listImportTemplates } from "@/services/data-transfer.service";

export default function AssetImportDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation(["assets", "common"]);
  const lang = useApiLanguage();
  const templatesQuery = useQuery({
    queryKey: ["datatransfer", "templates", lang],
    queryFn: () => listImportTemplates(lang),
    retry: false,
  });
  const template = templatesQuery.data?.find((item) => item.code === "assets.zimmet");

  return (
    <Modal opened onClose={onClose} title={t("assets:import.title")} size="lg">
      <Stack gap="md">
        <Text size="sm" c="dimmed">{t("assets:import.description")}</Text>
        {templatesQuery.isLoading ? (
          <Stack role="status" align="center" gap="sm" py="xl">
            <Loader size="sm" />
            <Text size="sm" c="dimmed">{t("assets:import.loading")}</Text>
          </Stack>
        ) : templatesQuery.isError ? (
          <Alert color="red" icon={<AlertTriangle size={16} />}>
            <Stack gap="sm" align="flex-start">
              <Text size="sm">{t("assets:import.loadError")}</Text>
              <Button variant="default" onClick={() => void templatesQuery.refetch()}>
                {t("assets:import.retry")}
              </Button>
            </Stack>
          </Alert>
        ) : template ? (
          <TemplateCard template={template} />
        ) : (
          <Alert color="yellow" icon={<AlertTriangle size={16} />}>
            {t("assets:import.unavailable")}
          </Alert>
        )}
      </Stack>
    </Modal>
  );
}
