import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useParams, useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { SortingState } from "@tanstack/react-table";
import { ArrowLeft, Download, Play } from "lucide-react";
import { Alert, Button, Card, Group, Skeleton, Stack, Text, Title } from "@mantine/core";
import { useDebouncedValue } from "@mantine/hooks";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { reportLabelKey } from "@/components/reports/report-catalog";
import { ReportDataGrid } from "@/components/reports/report-data-grid";
import { ReportParameterForm } from "@/components/reports/report-parameter-form";
import {
  allSatisfied,
  valuesFromSearchParams,
  valuesToSearchParams,
  type ParameterValues,
} from "@/components/reports/report-parameters";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import {
  downloadReportExecution,
  getReportCatalog,
  getReportExecution,
  getReportExecutionData,
  REPORT_PARAMETER_KEYS,
  REPORT_PREVIEW_UNAVAILABLE_CODE,
  requestReport,
  saveDownloadedReportFile,
  type ReportCatalogItem,
} from "@/services/reports.service";

const DEFAULT_PAGE_SIZE = 50;
const SEARCH_DEBOUNCE_MS = 300;
const EXECUTION_PARAM = "execution";

/**
 * Generic full-page report (roadmap #70): opens from the catalog with the chosen filters in the address, generates the
 * report once (no e-mail - the user is looking at it), shows the workbook as a grid and downloads that same execution's
 * Excel file. `?execution=<id>` pins the result so a refresh or a shared link restores it without generating again.
 */
export default function ReportViewerPage() {
  const { t } = useTranslation(["reports"]);
  const { key = "" } = useParams();
  const canViewCatalog = usePermission("reports.catalog.view");
  const canViewHistory = usePermission("reports.my.executions");
  const { data: catalog, isLoading } = useQuery({
    queryKey: ["reports", "catalog"],
    queryFn: getReportCatalog,
    enabled: canViewCatalog,
  });

  if (!canViewCatalog || !canViewHistory) return <NoAccess />;

  const report = catalog?.find((item) => item.key === key);

  return (
    <div className="space-y-4 p-4 md:p-6">
      <Button
        component={Link}
        to="/app/reports"
        variant="subtle"
        size="compact-sm"
        leftSection={<ArrowLeft size={16} />}
      >
        {t("reports:viewer.back")}
      </Button>
      {isLoading ? (
        <Skeleton h={160} />
      ) : !report ? (
        <Alert color="red" variant="light">
          {t("reports:viewer.notFound")}
        </Alert>
      ) : (
        <ReportViewer key={report.key} report={report} />
      )}
    </div>
  );
}

function ReportViewer({ report }: { report: ReportCatalogItem }) {
  const { t } = useTranslation(["reports", "common"]);
  const canGenerate = usePermission("reports.executions.generate");
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const today = useMemo(() => new Date(), []);
  const [values, setValues] = useState<ParameterValues>(() =>
    valuesFromSearchParams(searchParams, report.parameters, today)
  );
  const executionId = searchParams.get(EXECUTION_PARAM);
  const autoRunStarted = useRef(false);
  const [runError, setRunError] = useState<string | null>(null);

  const [sheet, setSheet] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [searchInput, setSearchInput] = useState("");
  const [search] = useDebouncedValue(searchInput.trim(), SEARCH_DEBOUNCE_MS);
  const [sorting, setSorting] = useState<SortingState>([]);
  const sort = sorting[0] ? `${sorting[0].desc ? "-" : ""}${sorting[0].id}` : undefined;

  const canRun = canGenerate && allSatisfied(report.parameters, values);

  const runMutation = useMutation({
    mutationFn: () => requestReport(report.key, values, { notify: false }),
    onSuccess: (execution) => {
      void queryClient.invalidateQueries({ queryKey: ["reports", "executions"] });
      if (execution.status === "failed") {
        setRunError(execution.errorMessage ?? t("reports:viewer.runError"));
        return;
      }
      queryClient.setQueryData(["reports", "execution", execution.id], execution);
      setSheet(0);
      setPage(1);
      setSorting([]);
      setSearchInput("");
      const next = valuesToSearchParams(values);
      next.set(EXECUTION_PARAM, execution.id);
      setSearchParams(next, { replace: true });
    },
    onError: (error) => setRunError(getApiErrorMessage(error, t("reports:viewer.runError"))),
  });

  // Open = run: the report is generated once as soon as the filters are complete and no execution is pinned in the address.
  useEffect(() => {
    if (executionId || autoRunStarted.current || !canRun) return;
    autoRunStarted.current = true;
    runMutation.mutate();
  }, [executionId, canRun, runMutation]);

  const executionQuery = useQuery({
    queryKey: ["reports", "execution", executionId],
    queryFn: () => getReportExecution(executionId ?? ""),
    enabled: executionId !== null,
    retry: false,
  });
  const execution =
    executionQuery.data && executionQuery.data.reportKey === report.key
      ? executionQuery.data
      : null;

  // A pinned execution that is gone (retention), foreign or of another report is dropped so the open-effect can run again.
  // A failed background refetch of an execution we already hold (e.g. the one we just generated) does not unpin it.
  const executionUnusable =
    executionId !== null &&
    ((executionQuery.isError && executionQuery.data === undefined) ||
      (executionQuery.isSuccess && execution === null));
  useEffect(() => {
    if (!executionUnusable) return;
    const next = new URLSearchParams(searchParams);
    next.delete(EXECUTION_PARAM);
    setSearchParams(next, { replace: true });
  }, [executionUnusable, searchParams, setSearchParams]);

  const succeeded = execution?.status === "succeeded";
  const dataQuery = useQuery({
    queryKey: ["reports", "execution-data", execution?.id, sheet, page, pageSize, search, sort],
    queryFn: () =>
      getReportExecutionData(execution?.id ?? "", {
        sheet,
        page,
        pageSize,
        q: search || undefined,
        sort,
      }),
    enabled: succeeded,
    placeholderData: (previous) => previous,
    retry: false,
  });
  const previewUnavailable =
    getApiProblem(dataQuery.error)?.code === REPORT_PREVIEW_UNAVAILABLE_CODE;
  const dataFailed = dataQuery.isError && !previewUnavailable;

  const downloadMutation = useMutation({
    mutationFn: () =>
      downloadReportExecution(
        execution?.id ?? "",
        execution?.resultFileName ?? `${report.key}.xlsx`
      ),
    onSuccess: saveDownloadedReportFile,
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("reports:history.downloadError")),
      }),
  });

  // PDF: the grid shows the Excel run; PDF is a separate run of the same filters with `format=pdf`, downloaded directly.
  const pdfMutation = useMutation({
    mutationFn: () =>
      requestReport(
        report.key,
        { ...values, [REPORT_PARAMETER_KEYS.format]: "pdf" },
        { notify: false }
      ),
    onSuccess: async (pdfExecution) => {
      void queryClient.invalidateQueries({ queryKey: ["reports", "executions"] });
      if (pdfExecution.status === "failed") {
        toast({
          variant: "destructive",
          title: t("common:error"),
          description: pdfExecution.errorMessage ?? t("reports:viewer.runError"),
        });
        return;
      }
      if (pdfExecution.status !== "succeeded") {
        toast({ variant: "success", title: t("reports:run.successNoDownload") });
        return;
      }
      saveDownloadedReportFile(
        await downloadReportExecution(
          pdfExecution.id,
          pdfExecution.resultFileName ?? `${report.key}.pdf`
        )
      );
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("reports:history.downloadError")),
      }),
  });

  const apply = () => {
    setRunError(null);
    runMutation.mutate();
  };

  const failedMessage =
    runError ??
    (execution?.status === "failed"
      ? (execution.errorMessage ?? t("reports:viewer.runError"))
      : null);
  const showIdleHint = !executionId && !runMutation.isPending && !failedMessage && !canRun;

  return (
    <Stack gap="md">
      <div>
        <Title order={1} size="h3">
          {t(reportLabelKey(report.key, "label"), { defaultValue: report.key })}
        </Title>
        <Text c="dimmed" size="sm" mt={4}>
          {t(reportLabelKey(report.key, "description"), { defaultValue: "" })}
        </Text>
      </div>

      <Card withBorder padding="md" radius="md">
        <Stack gap="md">
          <ReportParameterForm report={report} values={values} onChange={setValues} />
          <Group justify="flex-end" gap="sm">
            <Button
              type="button"
              variant="default"
              leftSection={<Download size={16} />}
              disabled={!succeeded}
              loading={downloadMutation.isPending}
              onClick={() => downloadMutation.mutate()}
            >
              {t("reports:viewer.downloadExcel")}
            </Button>
            {canGenerate && (
              <Button
                type="button"
                variant="default"
                leftSection={<Download size={16} />}
                disabled={!canRun}
                loading={pdfMutation.isPending}
                onClick={() => pdfMutation.mutate()}
              >
                {t("reports:viewer.downloadPdf")}
              </Button>
            )}
            {canGenerate && (
              <Button
                type="button"
                leftSection={<Play size={16} />}
                disabled={!canRun}
                loading={runMutation.isPending}
                onClick={apply}
              >
                {t("reports:viewer.apply")}
              </Button>
            )}
          </Group>
        </Stack>
      </Card>

      {failedMessage && (
        <Alert color="red" variant="light">
          {failedMessage}
        </Alert>
      )}
      {previewUnavailable && (
        <Alert color="gray" variant="light">
          {t("reports:viewer.previewUnavailable")}
        </Alert>
      )}
      {dataFailed && (
        <Alert color="red" variant="light">
          {getApiErrorMessage(dataQuery.error, t("reports:viewer.dataError"))}
        </Alert>
      )}
      {showIdleHint && (
        <Text size="sm" c="dimmed">
          {t("reports:viewer.idle")}
        </Text>
      )}

      {runMutation.isPending ? (
        <Skeleton h={240} radius="md" />
      ) : (
        succeeded &&
        !previewUnavailable &&
        !dataFailed && (
          <ReportDataGrid
            data={dataQuery.data}
            isLoading={dataQuery.isLoading}
            isFetching={dataQuery.isFetching}
            search={searchInput}
            onSearchChange={(value) => {
              setSearchInput(value);
              setPage(1);
            }}
            sorting={sorting}
            onSortingChange={(next) => {
              setSorting(next.slice(-1));
              setPage(1);
            }}
            onSheetChange={(index) => {
              setSheet(index);
              setPage(1);
              setSorting([]);
              setSearchInput("");
            }}
            onPageChange={setPage}
            pageSize={pageSize}
            onPageSizeChange={(size) => {
              setPageSize(size);
              setPage(1);
            }}
          />
        )
      )}
    </Stack>
  );
}
