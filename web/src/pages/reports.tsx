import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient, type UseMutationResult } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { BarChart3, Download, FileText, X } from "lucide-react";
import { Badge, Button, Card, Group, Input, Select, SimpleGrid, Skeleton, Stack, Text, Title } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { DatePicker } from "@/components/ui/date-picker";
import NoAccess from "@/components/no-access";
import PaginationFooter from "@/components/pagination-footer";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import { getLeaveUsageKpi, getWorkforceKpis, type KpiResult } from "@/services/analytics.service";
import {
  downloadReportExecution,
  getReportCatalog,
  listReportExecutions,
  requestReport,
  saveDownloadedReportFile,
  type DownloadedReportFile,
  type ReportExecution,
  type ReportExecutionStatus,
} from "@/services/reports.service";

interface BreakdownRow {
  id: string;
  kpiKey: string;
  kpiLabel: string;
  dimension: string;
  count: number;
  share: number;
}

function formatKpiValue(kpi: KpiResult, locale: string): string {
  if (kpi.unit === "ratio") {
    return new Intl.NumberFormat(locale, { style: "percent", minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(kpi.value);
  }
  return new Intl.NumberFormat(locale).format(kpi.value);
}

function formatShare(value: number, locale: string): string {
  return new Intl.NumberFormat(locale, { style: "percent", minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(value);
}

/** S-15 isn't deployed everywhere yet (faz-10 R0) - a 404 means "hide the card", not "show an error". */
function retryUnlessNotFound(failureCount: number, error: unknown): boolean {
  if (getApiErrorStatus(error) === 404) return false;
  return failureCount < 1;
}

const EXECUTION_STATUS_COLOR: Record<ReportExecutionStatus, string> = {
  queued: "gray",
  running: "blue",
  succeeded: "green",
  failed: "red",
};

export default function ReportsPage() {
  const { t } = useTranslation(["reports"]);
  const canViewKpis = usePermission("analytics.kpi.view");
  const canUseReports = useAnyPermission(["reports.catalog.view", "reports.my.executions"]);
  if (!canViewKpis && !canUseReports) return <NoAccess />;
  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <BarChart3 size={24} /> {t("reports:title")}
        </Title>
        <Text c="dimmed">{t("reports:subtitle")}</Text>
      </div>
      {canViewKpis && <WorkforceKpis />}
      {canUseReports && <ReportCatalogAndHistory />}
    </div>
  );
}

function WorkforceKpis() {
  const { t, i18n } = useTranslation(["reports", "common"]);
  const locale = i18n.language === "tr" ? "tr-TR" : "en-US";
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ["analytics", "workforce-kpis", from, to],
    queryFn: () => getWorkforceKpis({ from: from || undefined, to: to || undefined }),
    placeholderData: (previous) => previous,
  });

  function clearFilters() {
    setFrom("");
    setTo("");
  }

  const kpis = data ?? [];
  const breakdownRows: BreakdownRow[] = kpis.flatMap((kpi) =>
    kpi.breakdown.map((b, index) => ({
      id: `${kpi.key}-${index}`,
      kpiKey: kpi.key,
      kpiLabel: t(`reports:kpi.${kpi.key}.label`, { defaultValue: kpi.label }),
      dimension: b.dimension,
      count: b.count,
      share: b.share,
    })),
  );

  const columns: ColumnDef<BreakdownRow>[] = [
    {
      id: "kpiLabel",
      accessorKey: "kpiLabel",
      header: t("reports:breakdown.columns.kpi"),
      meta: { label: t("reports:breakdown.columns.kpi") },
    },
    {
      id: "dimension",
      accessorKey: "dimension",
      header: t("reports:breakdown.columns.dimension"),
      meta: { label: t("reports:breakdown.columns.dimension") },
    },
    {
      id: "count",
      accessorKey: "count",
      header: t("reports:breakdown.columns.count"),
      meta: { label: t("reports:breakdown.columns.count"), headerClassName: "w-32" },
      cell: ({ row }) => new Intl.NumberFormat(locale).format(row.original.count),
    },
    {
      id: "share",
      accessorKey: "share",
      header: t("reports:breakdown.columns.share"),
      meta: { label: t("reports:breakdown.columns.share"), headerClassName: "w-32" },
      cell: ({ row }) => formatShare(row.original.share, locale),
    },
  ];

  const table = useDataTable({ columns, data: breakdownRows, getRowId: (r) => r.id, pageSize: 10 });
  const hasFilters = from !== "" || to !== "";

  return (
    <Stack gap="md">
      <Title order={2} size="h4">
        {t("reports:kpiSection.title")}
      </Title>

      <Card withBorder padding="md">
        <Group align="flex-end" gap="sm">
          <Input.Wrapper id="reports-from" label={t("reports:filters.from")}>
            <DatePicker id="reports-from" value={from} max={to || undefined} clearable onChange={setFrom} />
          </Input.Wrapper>
          <Input.Wrapper id="reports-to" label={t("reports:filters.to")}>
            <DatePicker id="reports-to" value={to} min={from || undefined} clearable onChange={setTo} />
          </Input.Wrapper>
          {hasFilters && (
            <Button type="button" variant="subtle" size="sm" leftSection={<X size={16} />} onClick={clearFilters}>
              {t("reports:filters.clear")}
            </Button>
          )}
        </Group>
      </Card>

      <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
        {isLoading
          ? Array.from({ length: 3 }, (_, i) => (
              <Card key={i} withBorder padding="lg">
                <Skeleton h={16} w="60%" />
                <Skeleton h={28} mt={8} w="40%" />
              </Card>
            ))
          : kpis.map((kpi) => (
              <Card key={kpi.key} withBorder padding="lg">
                <Text size="xs" c="dimmed">
                  {t(`reports:kpi.${kpi.key}.label`, { defaultValue: kpi.label })}
                </Text>
                <Text size="xl" fw={700}>
                  {formatKpiValue(kpi, locale)}
                </Text>
              </Card>
            ))}
        {!isLoading && kpis.length === 0 && (
          <Card withBorder padding="lg">
            <Text c="dimmed" size="sm">
              {t("reports:empty")}
            </Text>
          </Card>
        )}
        <LeaveUsageKpiCard />
      </SimpleGrid>

      <Card withBorder padding={0}>
        <DataTable table={table} isLoading={isLoading} isFetching={isFetching} emptyMessage={t("reports:breakdown.empty")} />
      </Card>
    </Stack>
  );
}

/** İzin kullanım KPI'ı (S-15, faz-10 R0): backend ucu her ortamda yok - 404 gelirse kartı hiç göstermeyiz. */
function LeaveUsageKpiCard() {
  const { t, i18n } = useTranslation(["reports"]);
  const locale = i18n.language === "tr" ? "tr-TR" : "en-US";
  const year = new Date().getFullYear();

  const { data, isLoading, error } = useQuery({
    queryKey: ["analytics", "leave-usage-kpi", year],
    queryFn: () => getLeaveUsageKpi({ year }),
    retry: retryUnlessNotFound,
  });

  if (getApiErrorStatus(error) === 404) return null;

  return (
    <Card withBorder padding="lg">
      <Text size="xs" c="dimmed">
        {t("reports:kpi.leaveUsage.label")}
      </Text>
      {isLoading ? (
        <Skeleton h={28} mt={8} w="40%" />
      ) : !data ? (
        <Text c="dimmed" size="sm" mt={8}>
          {t("reports:empty")}
        </Text>
      ) : (
        <>
          <Text size="xl" fw={700}>
            {new Intl.NumberFormat(locale, { style: "percent", minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(data.usageRate)}
          </Text>
          <Text size="xs" c="dimmed" mt={4}>
            {t("reports:kpi.leaveUsage.detail", {
              used: new Intl.NumberFormat(locale).format(data.usedDays),
              total: new Intl.NumberFormat(locale).format(data.totalEntitledDays),
            })}
          </Text>
          {data.byLeaveType.length > 0 && (
            <Stack gap={2} mt="sm">
              {data.byLeaveType.map((row) => (
                <Group key={row.leaveTypeName} justify="space-between" gap="xs" wrap="nowrap">
                  <Text size="xs" c="dimmed" truncate>
                    {row.leaveTypeName}
                  </Text>
                  <Text size="xs">{new Intl.NumberFormat(locale).format(row.usedDays)}</Text>
                </Group>
              ))}
            </Stack>
          )}
        </>
      )}
    </Card>
  );
}

const EXECUTIONS_PAGE_SIZE = 10;

/** Report catalog (request a report) + the caller's own execution history, R0 synchronous generation. */
function ReportCatalogAndHistory() {
  const { t, i18n } = useTranslation(["reports", "common"]);
  const locale = i18n.language === "tr" ? "tr-TR" : "en-US";
  const canViewCatalog = usePermission("reports.catalog.view");
  const canGenerate = usePermission("reports.executions.generate");
  const canViewHistory = usePermission("reports.my.executions");
  const queryClient = useQueryClient();
  const [reportKey, setReportKey] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  // Narrow phones (faz-10 FE md.4): a card list reads better than a horizontally-scrolled table (missing-documents-report.tsx emsali).
  const isMobile = useMediaQuery("(max-width: 390px)");

  const { data: catalog, isLoading: catalogLoading } = useQuery({
    queryKey: ["reports", "catalog"],
    queryFn: getReportCatalog,
    enabled: canViewCatalog,
  });

  const { data: executions, isLoading: executionsLoading, isFetching: executionsFetching } = useQuery({
    queryKey: ["reports", "executions", page],
    queryFn: () => listReportExecutions({ page, pageSize: EXECUTIONS_PAGE_SIZE }),
    enabled: canViewHistory,
    placeholderData: (previous) => previous,
  });

  const requestMutation = useMutation({
    mutationFn: (key: string) => requestReport(key),
    onSuccess: (execution) => {
      queryClient.invalidateQueries({ queryKey: ["reports", "executions"] });
      if (execution.status === "failed") {
        toast({
          variant: "destructive",
          title: t("common:error"),
          description: execution.errorMessage ?? t("reports:catalog.generateError"),
        });
      } else {
        toast({ variant: "success", title: t("reports:catalog.generateSuccess") });
      }
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("reports:catalog.generateError")),
      }),
  });

  const downloadMutation = useMutation({
    mutationFn: (execution: ReportExecution) => downloadReportExecution(execution.id, execution.resultFileName ?? `${execution.reportKey}.dat`),
    onSuccess: saveDownloadedReportFile,
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("reports:history.downloadError")),
      }),
  });

  const columns: ColumnDef<ReportExecution>[] = [
    {
      id: "reportKey",
      accessorKey: "reportKey",
      header: t("reports:history.columns.report"),
      meta: { label: t("reports:history.columns.report") },
      cell: ({ row }) => t(`reports:catalog.${row.original.reportKey}.label`, { defaultValue: row.original.reportKey }),
    },
    {
      id: "status",
      accessorKey: "status",
      header: t("reports:history.columns.status"),
      meta: { label: t("reports:history.columns.status"), headerClassName: "w-32" },
      cell: ({ row }) => (
        <Badge variant="filled" color={EXECUTION_STATUS_COLOR[row.original.status]}>
          {t(`reports:history.status.${row.original.status}`)}
        </Badge>
      ),
    },
    {
      id: "requestedAt",
      accessorKey: "requestedAt",
      header: t("reports:history.columns.requestedAt"),
      meta: { label: t("reports:history.columns.requestedAt"), headerClassName: "w-48" },
      cell: ({ row }) => formatDateTime(row.original.requestedAt, locale),
    },
    {
      id: "download",
      header: "",
      meta: { headerClassName: "w-16" },
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
    <Stack gap="md">
      <Title order={2} size="h4" className="flex items-center gap-2">
        <FileText size={18} /> {t("reports:catalog.title")}
      </Title>

      {canViewCatalog && (
        <Card withBorder padding="md">
          <Group align="flex-end" gap="sm">
            <Select
              w={320}
              label={t("reports:catalog.selectLabel")}
              placeholder={t("reports:catalog.selectPlaceholder")}
              data={(catalog ?? []).map((item) => ({
                value: item.key,
                label: t(`reports:catalog.${item.key}.label`, { defaultValue: item.key }),
              }))}
              value={reportKey}
              onChange={setReportKey}
              disabled={catalogLoading || (catalog ?? []).length === 0}
              clearable
            />
            {canGenerate && (
              <Button
                type="button"
                disabled={!reportKey}
                loading={requestMutation.isPending}
                onClick={() => reportKey && requestMutation.mutate(reportKey)}
              >
                {t("reports:catalog.generate")}
              </Button>
            )}
          </Group>
          {!catalogLoading && (catalog ?? []).length === 0 && (
            <Text c="dimmed" size="sm" mt="sm">
              {t("reports:catalog.empty")}
            </Text>
          )}
        </Card>
      )}

      {canViewHistory && (
        <>
          {isMobile ? (
            <ReportExecutionsMobileList
              items={executions?.items ?? []}
              isLoading={executionsLoading}
              downloadMutation={downloadMutation}
            />
          ) : (
            <Card withBorder padding={0}>
              <DataTable table={table} isLoading={executionsLoading} isFetching={executionsFetching} emptyMessage={t("reports:history.empty")} />
            </Card>
          )}
          {executions && <PaginationFooter result={executions} onPageChange={setPage} />}
        </>
      )}
    </Stack>
  );
}

/** ≤390px kart listesi, masaüstü tablosunun aynı kolonlarını taşır (faz-10 FE md.4). */
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
  const locale = i18n.language === "tr" ? "tr-TR" : "en-US";

  if (isLoading) return <Skeleton h={160} />;

  if (items.length === 0) {
    return (
      <Card withBorder padding="lg">
        <Text size="sm" c="dimmed" ta="center">
          {t("reports:history.empty")}
        </Text>
      </Card>
    );
  }

  return (
    <Stack gap="sm">
      {items.map((item) => (
        <Card key={item.id} withBorder padding="md">
          <Stack gap={4}>
            <Group justify="space-between" align="center" wrap="nowrap">
              <Text size="sm" fw={500} truncate>
                {t(`reports:catalog.${item.reportKey}.label`, { defaultValue: item.reportKey })}
              </Text>
              <Badge variant="filled" color={EXECUTION_STATUS_COLOR[item.status]}>
                {t(`reports:history.status.${item.status}`)}
              </Badge>
            </Group>
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
