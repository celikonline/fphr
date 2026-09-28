import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useSearchParams } from "react-router";
import { useMutation, useQuery, type UseMutationResult } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { ArrowRight, BarChart3, Download, FileSpreadsheet } from "lucide-react";
import { Badge, Button, Card, Group, Skeleton, Stack, Tabs, Text, ThemeIcon, Title } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import PaginationFooter from "@/components/pagination-footer";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import { ReportCatalog, reportLabelKey } from "@/components/reports/report-catalog";
import { ReportParameterForm } from "@/components/reports/report-parameter-form";
import { allSatisfied, defaultValuesFor, valuesToSearchParams, type ParameterValues } from "@/components/reports/report-parameters";
import { WorkforceKpiPanel } from "@/components/reports/workforce-kpi-panel";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import {
  downloadReportExecution,
  getReportCatalog,
  getReportExecution,
  listReportExecutions,
  saveDownloadedReportFile,
  type DownloadedReportFile,
  type ReportCatalogItem,
  type ReportExecution,
  type ReportExecutionStatus,
} from "@/services/reports.service";

const DEFAULT_EXECUTIONS_PAGE_SIZE = 10;

const EXECUTION_STATUS_COLOR: Record<ReportExecutionStatus, string> = {
  queued: "gray",
  running: "blue",
  succeeded: "green",
  failed: "red",
};

/**
 * Raporlar (Kolay İK "Raporlar" menüsü, roadmap #70): kategori → rapor türü → filtreler → oluştur → indir, altında
 * "Son oluşturduklarım". İK göstergeleri (Faz 7 #99 KPI'ları) ayrı bir sekmede, yalnız gerçek veriden.
 */
export default function ReportsPage() {
  const { t } = useTranslation(["reports"]);
  const canViewKpis = usePermission("analytics.kpi.view");
  const canUseReports = useAnyPermission(["reports.catalog.view", "reports.my.executions"]);
  const [tab, setTab] = useState<string | null>(null);

  if (!canViewKpis && !canUseReports) return <NoAccess />;

  const showTabs = canViewKpis && canUseReports;
  const activeTab = tab ?? (canUseReports ? "reports" : "kpis");

  return (
    <div className="max-w-7xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <BarChart3 size={28} className="text-indigo-600" /> {t("reports:title")}
        </Title>
        <Text c="dimmed" size="sm" mt={4}>
          {t("reports:subtitle")}
        </Text>
      </div>

      {showTabs ? (
        <Tabs value={activeTab} onChange={setTab} keepMounted={false}>
          <Tabs.List>
            <Tabs.Tab value="reports" leftSection={<FileSpreadsheet size={16} />}>
              {t("reports:tabs.reports")}
            </Tabs.Tab>
            <Tabs.Tab value="kpis" leftSection={<BarChart3 size={16} />}>
              {t("reports:tabs.kpis")}
            </Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="reports" pt="lg">
            <ReportCatalogAndHistory />
          </Tabs.Panel>
          <Tabs.Panel value="kpis" pt="lg">
            <WorkforceKpiPanel />
          </Tabs.Panel>
        </Tabs>
      ) : canUseReports ? (
        <ReportCatalogAndHistory />
      ) : (
        <WorkforceKpiPanel />
      )}
    </div>
  );
}

/** Category-grouped catalog + parameter form + the caller's own execution history. */
function ReportCatalogAndHistory() {
  const { t, i18n } = useTranslation(["reports", "common"]);
  const locale = i18n.language === "tr" ? "tr-TR" : i18n.language === "de" ? "de-DE" : "en-US";
  const canViewCatalog = usePermission("reports.catalog.view");
  const canGenerate = usePermission("reports.executions.generate");
  const canViewHistory = usePermission("reports.my.executions");
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_EXECUTIONS_PAGE_SIZE);
  const isMobile = useMediaQuery("(max-width: 640px)");
  const [searchParams, setSearchParams] = useSearchParams();
  const emailExecutionHandled = useRef(false);

  const { data: catalog, isLoading: catalogLoading } = useQuery({
    queryKey: ["reports", "catalog"],
    queryFn: getReportCatalog,
    enabled: canViewCatalog,
  });

  const { data: executions, isLoading: executionsLoading, isFetching: executionsFetching } = useQuery({
    queryKey: ["reports", "executions", page, pageSize],
    queryFn: () => listReportExecutions({ page, pageSize }),
    enabled: canViewHistory,
    placeholderData: (previous) => previous,
  });

  const downloadMutation = useMutation({
    mutationFn: (execution: ReportExecution) =>
      downloadReportExecution(execution.id, execution.resultFileName ?? `${execution.reportKey}.xlsx`),
    onSuccess: saveDownloadedReportFile,
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("reports:history.downloadError")),
      }),
  });

  // Arrived from the "report ready" e-mail (?execution=<id>): offer that file right away, once.
  const emailExecutionId = searchParams.get("execution");
  useEffect(() => {
    if (!emailExecutionId || !canViewHistory || emailExecutionHandled.current) return;
    emailExecutionHandled.current = true;
    getReportExecution(emailExecutionId)
      .then((execution) => {
        if (execution.status === "succeeded") {
          downloadMutation.mutate(execution);
          toast({ variant: "success", title: t("reports:history.fromEmail") });
        }
      })
      .catch(() => {
        /* execution gone (retention) or not ours - the history table below is the fallback */
      })
      .finally(() => {
        const next = new URLSearchParams(searchParams);
        next.delete("execution");
        setSearchParams(next, { replace: true });
      });
  }, [emailExecutionId, canViewHistory, downloadMutation, searchParams, setSearchParams, t]);

  const selected = useMemo(() => (catalog ?? []).find((c) => c.key === selectedKey) ?? null, [catalog, selectedKey]);

  const columns: ColumnDef<ReportExecution>[] = [
    {
      id: "reportKey",
      accessorKey: "reportKey",
      header: t("reports:history.columns.report"),
      meta: { label: t("reports:history.columns.report") },
      cell: ({ row }) => t(reportLabelKey(row.original.reportKey, "label"), { defaultValue: row.original.reportKey }),
    },
    {
      id: "status",
      accessorKey: "status",
      header: t("reports:history.columns.status"),
      meta: { label: t("reports:history.columns.status"), headerClassName: "w-32" },
      cell: ({ row }) => (
        <Badge variant="light" color={EXECUTION_STATUS_COLOR[row.original.status]}>
          {t(`reports:history.status.${row.original.status}`)}
        </Badge>
      ),
    },
    {
      id: "requestedAt",
      accessorKey: "requestedAt",
      header: t("reports:history.columns.requestedAt"),
      meta: { label: t("reports:history.columns.requestedAt"), headerClassName: "w-44" },
      cell: ({ row }) => formatDateTime(row.original.requestedAt, locale),
    },
    {
      id: "file",
      header: t("reports:history.columns.file"),
      meta: { label: t("reports:history.columns.file") },
      cell: ({ row }) =>
        row.original.status === "failed" ? (
          <Text size="xs" c="red.7" truncate maw={280} title={row.original.errorMessage ?? undefined}>
            {row.original.errorMessage ?? t("reports:run.error")}
          </Text>
        ) : (
          <Text size="xs" c="dimmed" truncate maw={280}>
            {row.original.resultFileName ?? ""}
          </Text>
        ),
    },
    {
      id: "download",
      header: "",
      meta: { headerClassName: "w-24" },
      cell: ({ row }) =>
        row.original.status === "succeeded" ? (
          <Button
            type="button"
            variant="subtle"
            size="xs"
            leftSection={<Download size={14} />}
            loading={downloadMutation.isPending && downloadMutation.variables?.id === row.original.id}
            onClick={() => downloadMutation.mutate(row.original)}
          >
            {t("reports:history.download")}
          </Button>
        ) : null,
    },
  ];

  const table = useDataTable({ columns, data: executions?.items ?? [], getRowId: (r) => r.id });

  return (
    <Stack gap="xl">
      {canViewCatalog && (
        <div className="grid gap-4 lg:grid-cols-12">
          <div className="lg:col-span-7">
            <ReportCatalog items={catalog ?? []} isLoading={catalogLoading} selectedKey={selectedKey} onSelect={setSelectedKey} />
          </div>

          <div className="lg:col-span-5">
            {selected ? (
              // The full-page viewer needs reports.my.executions as well, so the open button requires both.
              <ReportRunPanel key={selected.key} report={selected} canGenerate={canGenerate && canViewHistory} />
            ) : (
              <Card withBorder padding="lg" radius="md" className="h-full">
                <Stack align="center" justify="center" gap="xs" className="min-h-40">
                  <ThemeIcon variant="light" color="gray" size={40} radius="xl">
                    <FileSpreadsheet size={20} />
                  </ThemeIcon>
                  <Text c="dimmed" size="sm" ta="center">
                    {t("reports:catalog.selectHint")}
                  </Text>
                </Stack>
              </Card>
            )}
          </div>
        </div>
      )}

      {canViewHistory && (
        <Stack gap="sm">
          <Title order={2} size="h4">
            {t("reports:history.title")}
          </Title>
          {isMobile ? (
            <ReportExecutionsMobileList items={executions?.items ?? []} isLoading={executionsLoading} downloadMutation={downloadMutation} />
          ) : (
            <Card withBorder padding={0} radius="md">
              <DataTable table={table} isLoading={executionsLoading} isFetching={executionsFetching} emptyMessage={t("reports:history.empty")} />
            </Card>
          )}
          {executions && (
            <PaginationFooter
              result={executions}
              onPageChange={setPage}
              pageSize={pageSize}
              onPageSizeChange={(size) => {
                setPageSize(size);
                setPage(1);
              }}
            />
          )}
        </Stack>
      )}
    </Stack>
  );
}

/** Kolay İK's "rapor türü → filtreler" step; the report itself opens on its own full page (`/app/reports/:key`). */
function ReportRunPanel({ report, canGenerate }: { report: ReportCatalogItem; canGenerate: boolean }) {
  const { t } = useTranslation(["reports"]);
  const navigate = useNavigate();
  const today = useMemo(() => new Date(), []);
  const [values, setValues] = useState<ParameterValues>(() => defaultValuesFor(report.parameters, today));

  const openReport = () => {
    const query = valuesToSearchParams(values).toString();
    void navigate(`/app/reports/${report.key}${query ? `?${query}` : ""}`);
  };

  return (
    <Card withBorder padding="lg" radius="md">
      <Stack gap="md">
        <div>
          <Text size="xs" c="dimmed" tt="uppercase" fw={600} lts={0.5}>
            {t("reports:run.title")}
          </Text>
          <Text fw={700} size="md" mt={2}>
            {t(reportLabelKey(report.key, "label"), { defaultValue: report.key })}
          </Text>
          <Text size="xs" c="dimmed" mt={4}>
            {t(reportLabelKey(report.key, "description"), { defaultValue: "" })}
          </Text>
        </div>

        <ReportParameterForm report={report} values={values} onChange={setValues} />

        {canGenerate && (
          <Group justify="flex-end">
            <Button type="button" rightSection={<ArrowRight size={16} />} disabled={!allSatisfied(report.parameters, values)} onClick={openReport}>
              {t("reports:run.open")}
            </Button>
          </Group>
        )}
      </Stack>
    </Card>
  );
}

/** Mobile report executions list */
function ReportExecutionsMobileList({
  items,
  isLoading,
  downloadMutation,
}: {
  items: ReportExecution[];
  isLoading: boolean;
  downloadMutation: UseMutationResult<DownloadedReportFile, Error, ReportExecution>;
}) {
  const { t, i18n } = useTranslation(["reports", "common"]);
  const locale = i18n.language === "tr" ? "tr-TR" : i18n.language === "de" ? "de-DE" : "en-US";

  if (isLoading) return <Skeleton h={160} />;

  if (items.length === 0) {
    return (
      <Card withBorder padding="lg" radius="md">
        <Text size="sm" c="dimmed" ta="center">
          {t("reports:history.empty")}
        </Text>
      </Card>
    );
  }

  return (
    <Stack gap="sm">
      {items.map((item) => (
        <Card key={item.id} withBorder padding="md" radius="md">
          <Stack gap={4}>
            <Group justify="space-between" align="center" wrap="nowrap">
              <Text size="sm" fw={500} truncate>
                {t(reportLabelKey(item.reportKey, "label"), { defaultValue: item.reportKey })}
              </Text>
              <Badge variant="light" color={EXECUTION_STATUS_COLOR[item.status]}>
                {t(`reports:history.status.${item.status}`)}
              </Badge>
            </Group>
            {item.status === "failed" && item.errorMessage && (
              <Text size="xs" c="red.7">
                {item.errorMessage}
              </Text>
            )}
            <Group justify="space-between" align="center" wrap="wrap" mt={4}>
              <Text size="xs" c="dimmed">
                {formatDateTime(item.requestedAt, locale)}
              </Text>
              {item.status === "succeeded" && (
                <Button
                  type="button"
                  variant="subtle"
                  size="xs"
                  leftSection={<Download size={14} />}
                  loading={downloadMutation.isPending && downloadMutation.variables?.id === item.id}
                  onClick={() => downloadMutation.mutate(item)}
                >
                  {t("reports:history.download")}
                </Button>
              )}
            </Group>
          </Stack>
        </Card>
      ))}
    </Stack>
  );
}
