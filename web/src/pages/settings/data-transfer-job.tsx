import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Button, Card, Group, Skeleton, Stack, Text, Title } from "@mantine/core";
import { ArrowLeft, FileSpreadsheet } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import ColumnMappingStep from "@/components/data-transfer/column-mapping-step";
import ImportResultStep from "@/components/data-transfer/import-result-step";
import { JobStatusBadge } from "@/components/data-transfer/job-badges";
import { useApiLanguage } from "@/components/data-transfer/use-api-language";
import { getApiErrorStatus } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import {
  DataTransferPermissions,
  FINISHED_JOB_STATUSES,
  getImportJob,
  listImportTemplates,
} from "@/services/data-transfer.service";

export default function DataTransferJobPage() {
  const canView = usePermission(DataTransferPermissions.View);
  if (!canView) return <NoAccess />;
  return <DataTransferJob />;
}

function DataTransferJob() {
  const { id = "" } = useParams<{ id: string }>();
  const { t, i18n } = useTranslation(["dataTransfer", "common", "assets"]);
  const lang = useApiLanguage();

  const jobQuery = useQuery({
    queryKey: ["datatransfer", "import-job", id],
    queryFn: () => getImportJob(id),
    enabled: id.length > 0,
    retry: (count, error) => getApiErrorStatus(error) !== 404 && count < 2,
  });

  const templatesQuery = useQuery({
    queryKey: ["datatransfer", "templates", lang],
    queryFn: () => listImportTemplates(lang),
  });

  const job = jobQuery.data;
  const template = job ? templatesQuery.data?.find((tpl) => tpl.code === job.templateCode) : undefined;
  const isFinished = job ? FINISHED_JOB_STATUSES.includes(job.status) : false;

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <Stack gap="sm" align="flex-start">
        <Button
          component={Link}
          to="/app/settings/data-transfer?tab=history"
          variant="subtle"
          size="sm"
          ml={-12}
          leftSection={<ArrowLeft size={16} />}
        >
          {t("dataTransfer:job.back")}
        </Button>
        {job?.templateCode === "assets.zimmet" && (
          <Button component={Link} to="/app/assets" variant="subtle" size="sm" ml={-12} leftSection={<ArrowLeft size={16} />}>
            {t("assets:import.back")}
          </Button>
        )}
        {job && (
          <Group justify="space-between" align="flex-start" gap="sm" w="100%">
            <div>
              <Title order={2} className="flex items-center gap-2">
                <FileSpreadsheet size={24} /> {job.fileName}
              </Title>
              <Text c="dimmed">
                {template?.name ?? job.templateCode} · {t("dataTransfer:job.uploadedAt")}{" "}
                {formatDateTime(job.createdAt, i18n.language)}
                {job.totalRows > 0 && ` · ${t("dataTransfer:job.rowCount", { count: job.totalRows })}`}
              </Text>
            </div>
            <JobStatusBadge status={job.status} />
          </Group>
        )}
      </Stack>

      {jobQuery.isLoading || (!isFinished && templatesQuery.isLoading) ? (
        <Stack gap="sm">
          <Skeleton h={40} />
          <Skeleton h={256} />
        </Stack>
      ) : jobQuery.isError || !job ? (
        <Card withBorder padding="lg">
          <Text size="sm" c="dimmed" ta="center">
            {getApiErrorStatus(jobQuery.error) === 404 ? t("dataTransfer:job.notFound") : t("dataTransfer:job.loadError")}
          </Text>
        </Card>
      ) : isFinished ? (
        <ImportResultStep job={job} template={template} />
      ) : !template ? (
        <Card withBorder padding="lg">
          <Text size="sm" c="red" ta="center">
            {t("dataTransfer:job.templateUnavailable", { code: job.templateCode })}
          </Text>
        </Card>
      ) : (
        <ColumnMappingStep job={job} template={template} />
      )}
    </div>
  );
}
