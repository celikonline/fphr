import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, FileSpreadsheet } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
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
  const { t, i18n } = useTranslation(["dataTransfer", "common"]);
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
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div className="space-y-3">
        <Button asChild variant="ghost" size="sm" className="-ml-3">
          <Link to="/app/settings/data-transfer?tab=history">
            <ArrowLeft className="mr-2 h-4 w-4" /> {t("dataTransfer:job.back")}
          </Link>
        </Button>
        {job && (
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <h1 className="flex items-center gap-2 text-2xl font-bold">
                <FileSpreadsheet className="h-6 w-6" /> {job.fileName}
              </h1>
              <p className="text-muted-foreground">
                {template?.name ?? job.templateCode} · {t("dataTransfer:job.uploadedAt")}{" "}
                {formatDateTime(job.createdAt, i18n.language)}
                {job.totalRows > 0 && ` · ${t("dataTransfer:job.rowCount", { count: job.totalRows })}`}
              </p>
            </div>
            <JobStatusBadge status={job.status} />
          </div>
        )}
      </div>

      {jobQuery.isLoading || (!isFinished && templatesQuery.isLoading) ? (
        <div className="space-y-3">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-64 w-full" />
        </div>
      ) : jobQuery.isError || !job ? (
        <Card>
          <CardContent className="p-6 text-center text-sm text-muted-foreground">
            {getApiErrorStatus(jobQuery.error) === 404 ? t("dataTransfer:job.notFound") : t("dataTransfer:job.loadError")}
          </CardContent>
        </Card>
      ) : isFinished ? (
        <ImportResultStep job={job} template={template} />
      ) : !template ? (
        <Card>
          <CardContent className="p-6 text-center text-sm text-destructive">
            {t("dataTransfer:job.templateUnavailable", { code: job.templateCode })}
          </CardContent>
        </Card>
      ) : (
        <ColumnMappingStep job={job} template={template} />
      )}
    </div>
  );
}
