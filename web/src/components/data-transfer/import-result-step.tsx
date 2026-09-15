import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { useMutation, useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { AlertTriangle, CheckCircle2, Download, Loader2, Upload, XCircle } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import PaginationFooter from "@/components/pagination-footer";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  DataTransferPermissions,
  IMPORT_ROW_OUTCOMES,
  downloadImportJobResult,
  listImportJobRows,
  saveDownloadedFile,
  type ImportJob,
  type ImportJobRow,
  type ImportRowOutcome,
  type ImportTemplate,
  type RowError,
} from "@/services/data-transfer.service";
import { RowOutcomeBadge } from "./job-badges";
import { useApiLanguage } from "./use-api-language";
import WizardSteps from "./wizard-steps";

const ALL = "all";
const DEFAULT_PAGE_SIZE = 20;

interface ImportResultStepProps {
  job: ImportJob;
  template: ImportTemplate | undefined;
}

function resultFileName(fileName: string, suffix: string): string {
  const base = fileName.replace(/\.[^.]+$/, "") || "import";
  return `${base}-${suffix}.xlsx`;
}

export default function ImportResultStep({ job, template }: ImportResultStepProps) {
  const { t } = useTranslation(["dataTransfer", "common"]);
  const navigate = useNavigate();
  const lang = useApiLanguage();
  const canDownloadResult = usePermission(DataTransferPermissions.ResultDownload);
  const [outcome, setOutcome] = useState<string>(ALL);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);

  // Rows are server-paged in row-number order (the API has no sort parameter), so columns are not sortable.
  const rowsQuery = useQuery({
    queryKey: ["datatransfer", "import-job-rows", job.id, outcome, page, pageSize],
    queryFn: () =>
      listImportJobRows(job.id, {
        outcome: outcome === ALL ? undefined : (outcome as ImportRowOutcome),
        page,
        pageSize,
      }),
    placeholderData: (previous) => previous,
  });

  function changePageSize(size: number) {
    setPageSize(size);
    setPage(1);
  }

  const downloadMutation = useMutation({
    mutationFn: () => downloadImportJobResult(job.id, resultFileName(job.fileName, t("dataTransfer:result.fileSuffix")), lang),
    onSuccess: saveDownloadedFile,
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("dataTransfer:result.downloadError")),
      }),
  });

  const columnLabel = (column: string | null) => {
    if (!column) return null;
    return template?.columns.find((c) => c.key === column)?.header ?? column;
  };

  const errorText = (error: RowError) => error.message || t(`dataTransfer:errorCodes.${error.code}`, error.code);

  const columns: ColumnDef<ImportJobRow>[] = [
    {
      id: "rowNumber",
      enableSorting: false,
      header: t("dataTransfer:result.rowNumber"),
      meta: { headerClassName: "w-24 pl-6", cellClassName: "pl-6 tabular-nums" },
      cell: ({ row }) => row.original.rowNumber,
    },
    {
      id: "matchKey",
      enableSorting: false,
      header: t("dataTransfer:result.matchKey"),
      cell: ({ row }) => row.original.matchKey ?? "—",
    },
    {
      id: "outcome",
      enableSorting: false,
      header: t("dataTransfer:result.outcome"),
      cell: ({ row }) => <RowOutcomeBadge outcome={row.original.outcome} />,
    },
    {
      id: "errors",
      enableSorting: false,
      header: t("dataTransfer:result.errorDescription"),
      meta: { headerClassName: "pr-6", cellClassName: "pr-6" },
      cell: ({ row }) =>
        row.original.errors.length === 0 ? (
          <span className="text-muted-foreground">—</span>
        ) : (
          <ul className="space-y-0.5 text-sm">
            {row.original.errors.map((error, i) => (
              <li key={i}>
                {columnLabel(error.column) && <span className="font-medium">{columnLabel(error.column)}: </span>}
                {errorText(error)}
              </li>
            ))}
          </ul>
        ),
    },
  ];

  const table = useDataTable({
    columns,
    data: rowsQuery.data?.items ?? [],
    getRowId: (row) => String(row.rowNumber),
  });

  const counters = [
    { key: "total", value: job.totalRows, className: "" },
    { key: "inserted", value: job.insertedCount, className: "text-emerald-600" },
    { key: "updated", value: job.updatedCount, className: "text-primary" },
    { key: "skipped", value: job.skippedCount, className: "text-muted-foreground" },
    { key: "errors", value: job.errorCount, className: job.errorCount > 0 ? "text-destructive" : "" },
  ];

  return (
    <div className="space-y-6">
      <WizardSteps active={["result"]} />

      {job.status === "Completed" ? (
        <Alert variant="success">
          <CheckCircle2 className="h-4 w-4" />
          <AlertTitle>{t("dataTransfer:result.completed")}</AlertTitle>
        </Alert>
      ) : job.status === "CompletedWithErrors" ? (
        <Alert variant="warning">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("dataTransfer:result.rowsWithErrors", { count: job.errorCount })}</AlertTitle>
          <AlertDescription>{t("dataTransfer:result.completedWithErrorsHint")}</AlertDescription>
        </Alert>
      ) : (
        <Alert variant="destructive">
          <XCircle className="h-4 w-4" />
          <AlertTitle>{t("dataTransfer:result.failed")}</AlertTitle>
          <AlertDescription>
            {job.errorCount > 0 && `${t("dataTransfer:result.rowsWithErrors", { count: job.errorCount })} `}
            {job.failurePolicy === "AllOrNothing"
              ? t("dataTransfer:result.failedAllOrNothingHint")
              : t("dataTransfer:result.failedHint")}
          </AlertDescription>
        </Alert>
      )}

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-5">
        {counters.map((counter) => (
          <Card key={counter.key}>
            <CardContent className="p-4">
              <p className="text-xs text-muted-foreground">{t(`dataTransfer:counts.${counter.key}`)}</p>
              <p className={`text-2xl font-semibold tabular-nums ${counter.className}`}>{counter.value}</p>
            </CardContent>
          </Card>
        ))}
      </div>

      <div className="flex flex-wrap gap-2">
        {canDownloadResult && (
          <Button type="button" onClick={() => downloadMutation.mutate()} disabled={downloadMutation.isPending}>
            {downloadMutation.isPending ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <Download className="mr-2 h-4 w-4" />
            )}
            {t("dataTransfer:result.download")}
          </Button>
        )}
        <Button type="button" variant="outline" onClick={() => navigate("/app/settings/data-transfer")}>
          <Upload className="mr-2 h-4 w-4" /> {t("dataTransfer:result.newUpload")}
        </Button>
      </div>

      <Card>
        <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-3 space-y-0">
          <CardTitle className="text-lg">{t("dataTransfer:result.rowsTitle")}</CardTitle>
          <Tabs
            value={outcome}
            onValueChange={(value) => {
              setOutcome(value);
              setPage(1);
            }}
          >
            <TabsList className="h-auto flex-wrap">
              <TabsTrigger value={ALL}>{t("dataTransfer:outcomeFilter.all")}</TabsTrigger>
              {IMPORT_ROW_OUTCOMES.map((value) => (
                <TabsTrigger key={value} value={value}>
                  {t(`dataTransfer:outcomeFilter.${value}`)}
                </TabsTrigger>
              ))}
            </TabsList>
          </Tabs>
        </CardHeader>
        <CardContent className="p-0">
          {rowsQuery.isError ? (
            <p className="p-6 text-center text-sm text-destructive">{t("dataTransfer:result.rowsLoadError")}</p>
          ) : (
            <>
              <DataTable
                table={table}
                isLoading={rowsQuery.isLoading}
                isFetching={rowsQuery.isFetching}
                emptyMessage={t("dataTransfer:result.rowsEmpty")}
              />
              {rowsQuery.data && (
                <PaginationFooter
                  result={rowsQuery.data}
                  onPageChange={setPage}
                  pageSize={pageSize}
                  onPageSizeChange={changePageSize}
                />
              )}
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
