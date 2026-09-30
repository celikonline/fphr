import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";
import { useMutation, useQuery, useQueryClient, type UseMutationResult } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { BarChart3, Check, Download, FileSpreadsheet } from "lucide-react";
import {
  Alert,
  Badge,
  Button,
  Card,
  Group,
  Input,
  MultiSelect,
  SegmentedControl,
  Select,
  Skeleton,
  Stack,
  Switch,
  Tabs,
  Text,
  ThemeIcon,
  Title,
} from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { DatePicker } from "@/components/ui/date-picker";
import NoAccess from "@/components/no-access";
import PaginationFooter from "@/components/pagination-footer";
import { useAnyPermission, usePermission } from "@/components/permission-guard";
import { useOrgUnits } from "@/components/employees/use-org-units";
import { ReportCatalog, reportLabelKey } from "@/components/reports/report-catalog";
import { WorkforceKpiPanel } from "@/components/reports/workforce-kpi-panel";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateTime, toDateOnly } from "@/lib/dates";
import {
  downloadReportExecution,
  getReportCatalog,
  getReportExecution,
  listReportExecutions,
  REPORT_PARAMETER_KEYS,
  requestReport,
  saveDownloadedReportFile,
  type DownloadedReportFile,
  type ReportCatalogItem,
  type ReportExecution,
  type ReportExecutionStatus,
  type ReportParameter,
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
  const queryClient = useQueryClient();
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
              <ReportRunPanel
                key={selected.key}
                report={selected}
                canGenerate={canGenerate}
                onGenerated={() => queryClient.invalidateQueries({ queryKey: ["reports", "executions"] })}
              />
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

type ParameterValues = Record<string, string>;

/** Çıktı biçimi: `format` rapor parametresi olarak gönderilir (varsayılan Excel). */
const REPORT_FORMATS = ["xlsx", "pdf"] as const;
type ReportFormat = (typeof REPORT_FORMATS)[number];
const REPORT_FORMAT_LABELS: Record<ReportFormat, string> = { xlsx: "Excel", pdf: "PDF" };

function defaultValuesFor(parameters: ReportParameter[], today: Date): ParameterValues {
  const values: ParameterValues = {};
  for (const parameter of parameters) {
    if (!parameter.isRequired) continue;
    if (parameter.kind === "year") values[REPORT_PARAMETER_KEYS.year] = String(today.getFullYear());
    if (parameter.kind === "dateRange") {
      values[REPORT_PARAMETER_KEYS.from] = toDateOnly(new Date(today.getFullYear(), today.getMonth(), 1));
      values[REPORT_PARAMETER_KEYS.to] = toDateOnly(new Date(today.getFullYear(), today.getMonth() + 1, 0));
    }
  }
  return values;
}

function isSatisfied(parameter: ReportParameter, values: ParameterValues): boolean {
  if (!parameter.isRequired) return true;
  if (parameter.kind === "dateRange") return Boolean(values[REPORT_PARAMETER_KEYS.from] && values[REPORT_PARAMETER_KEYS.to]);
  return Boolean(values[parameter.key]);
}

/** Kolay İK's "rapor türü → Birimler / Ay / Yıl → oluştur" step, drawn from the report's parameter descriptors. */
function ReportRunPanel({ report, canGenerate, onGenerated }: { report: ReportCatalogItem; canGenerate: boolean; onGenerated: () => void }) {
  const { t, i18n } = useTranslation(["reports", "common"]);
  const today = useMemo(() => new Date(), []);
  const [values, setValues] = useState<ParameterValues>(() => defaultValuesFor(report.parameters, today));
  const [format, setFormat] = useState<ReportFormat>("xlsx");
  const { options: orgUnitOptions } = useOrgUnits();
  const hasRequired = report.parameters.some((p) => p.isRequired);
  const allSatisfied = report.parameters.every((p) => isSatisfied(p, values));

  const set = (key: string, value: string | null | undefined) =>
    setValues((prev) => {
      const next = { ...prev };
      if (value === null || value === undefined || value === "") delete next[key];
      else next[key] = value;
      return next;
    });

  const generateMutation = useMutation({
    mutationFn: () => requestReport(report.key, { ...values, format }),
    onSuccess: async (execution) => {
      onGenerated();
      if (execution.status === "failed") {
        toast({ variant: "destructive", title: t("common:error"), description: execution.errorMessage ?? t("reports:run.error") });
        return;
      }
      if (execution.status === "succeeded") {
        try {
          saveDownloadedReportFile(await downloadReportExecution(execution.id, execution.resultFileName ?? `${report.key}.${format}`));
          toast({ variant: "success", title: t("reports:run.success") });
        } catch {
          toast({ variant: "success", title: t("reports:run.successNoDownload") });
        }
      } else {
        toast({ variant: "success", title: t("reports:run.successNoDownload") });
      }
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("reports:run.error")) }),
  });

  const monthOptions = useMemo(
    () =>
      Array.from({ length: 12 }, (_, i) => ({
        value: String(i + 1),
        label: new Intl.DateTimeFormat(i18n.language, { month: "long" }).format(new Date(2000, i, 1)),
      })),
    [i18n.language],
  );
  const yearOptions = useMemo(() => Array.from({ length: 7 }, (_, i) => String(today.getFullYear() + 1 - i)), [today]);
  const depthById = useMemo(() => new Map(orgUnitOptions.map((u) => [u.id, u.depth])), [orgUnitOptions]);

  const applyPreset = (preset: "thisMonth" | "lastMonth" | "thisYear") => {
    const y = today.getFullYear();
    const m = today.getMonth();
    const [from, to] =
      preset === "thisMonth"
        ? [new Date(y, m, 1), new Date(y, m + 1, 0)]
        : preset === "lastMonth"
          ? [new Date(y, m - 1, 1), new Date(y, m, 0)]
          : [new Date(y, 0, 1), new Date(y, 11, 31)];
    setValues((prev) => ({ ...prev, [REPORT_PARAMETER_KEYS.from]: toDateOnly(from), [REPORT_PARAMETER_KEYS.to]: toDateOnly(to) }));
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

        {report.parameters.length === 0 ? (
          <Text size="sm" c="dimmed">
            {t("reports:run.noFilters")}
          </Text>
        ) : (
          <Stack gap="sm">
            {report.parameters.map((parameter) => {
              // Çıktı biçimi tek yerde (aşağıdaki SegmentedControl) çizilir.
              if (parameter.key === REPORT_PARAMETER_KEYS.format) return null;
              const required = parameter.isRequired ? " *" : "";
              switch (parameter.kind) {
                case "orgUnits":
                  return (
                    <MultiSelect
                      key={parameter.key}
                      label={t("reports:params.orgUnitIds") + required}
                      description={t("reports:params.orgUnitIdsHint")}
                      placeholder={t("reports:params.orgUnitIdsPlaceholder")}
                      data={orgUnitOptions.map((u) => ({ value: u.id, label: u.name }))}
                      value={values[parameter.key] ? values[parameter.key].split(",") : []}
                      onChange={(ids) => set(parameter.key, ids.join(","))}
                      searchable
                      clearable
                      renderOption={({ option, checked }) => (
                        <Group gap="xs" wrap="nowrap">
                          <Check size={14} style={{ opacity: checked ? 1 : 0, flexShrink: 0 }} />
                          <span style={{ paddingLeft: `${(depthById.get(option.value) ?? 0) * 12}px` }}>{option.label}</span>
                        </Group>
                      )}
                    />
                  );
                case "year":
                  return (
                    <Select
                      key={parameter.key}
                      label={t("reports:params.year") + required}
                      data={yearOptions}
                      value={values[parameter.key] ?? null}
                      onChange={(v) => set(parameter.key, v)}
                      clearable={!parameter.isRequired}
                    />
                  );
                case "month":
                  return (
                    <Select
                      key={parameter.key}
                      label={t("reports:params.month") + required}
                      placeholder={t("reports:params.monthPlaceholder")}
                      data={monthOptions}
                      value={values[parameter.key] ?? null}
                      onChange={(v) => set(parameter.key, v)}
                      clearable={!parameter.isRequired}
                    />
                  );
                case "dateRange":
                  return (
                    <Stack key={parameter.key} gap={6}>
                      <Text size="sm" fw={500}>
                        {t("reports:params.dateRange") + required}
                      </Text>
                      <Group grow align="flex-end" gap="sm">
                        <Input.Wrapper id={`${report.key}-from`} label={t("reports:params.from")}>
                          <DatePicker
                            id={`${report.key}-from`}
                            value={values[REPORT_PARAMETER_KEYS.from] ?? ""}
                            max={values[REPORT_PARAMETER_KEYS.to] || undefined}
                            clearable={!parameter.isRequired}
                            onChange={(v) => set(REPORT_PARAMETER_KEYS.from, v)}
                          />
                        </Input.Wrapper>
                        <Input.Wrapper id={`${report.key}-to`} label={t("reports:params.to")}>
                          <DatePicker
                            id={`${report.key}-to`}
                            value={values[REPORT_PARAMETER_KEYS.to] ?? ""}
                            min={values[REPORT_PARAMETER_KEYS.from] || undefined}
                            clearable={!parameter.isRequired}
                            onChange={(v) => set(REPORT_PARAMETER_KEYS.to, v)}
                          />
                        </Input.Wrapper>
                      </Group>
                      <Group gap={6}>
                        {(["thisMonth", "lastMonth", "thisYear"] as const).map((preset) => (
                          <Button key={preset} type="button" size="compact-xs" variant="light" color="gray" onClick={() => applyPreset(preset)}>
                            {t(`reports:params.presets.${preset}`)}
                          </Button>
                        ))}
                      </Group>
                    </Stack>
                  );
                case "boolean":
                  return (
                    <Switch
                      key={parameter.key}
                      label={t(`reports:params.${parameter.key}`, { defaultValue: parameter.key })}
                      checked={values[parameter.key] === "true"}
                      onChange={(e) => set(parameter.key, e.currentTarget.checked ? "true" : null)}
                    />
                  );
                case "select":
                  return (
                    <Select
                      key={parameter.key}
                      label={t(`reports:params.${parameter.key}`, { defaultValue: parameter.key }) + required}
                      data={(parameter.options ?? []).map((option) => ({
                        value: option,
                        label: t(`reports:params.${parameter.key}Options.${option}`, { defaultValue: option }),
                      }))}
                      value={values[parameter.key] ?? null}
                      onChange={(v) => set(parameter.key, v)}
                      clearable={!parameter.isRequired}
                    />
                  );
                default:
                  return null;
              }
            })}
            {hasRequired && (
              <Text size="xs" c="dimmed">
                {t("reports:run.requiredHint")}
              </Text>
            )}
          </Stack>
        )}

        <Group justify="space-between" align="center" wrap="wrap" gap="sm">
          <SegmentedControl
            size="xs"
            aria-label={t("reports:run.format")}
            value={format}
            onChange={(value) => setFormat(value as ReportFormat)}
            data={REPORT_FORMATS.map((f) => ({ value: f, label: REPORT_FORMAT_LABELS[f] }))}
          />
          {canGenerate && (
            <Button
              type="button"
              leftSection={<Download size={16} />}
              disabled={!allSatisfied}
              loading={generateMutation.isPending}
              onClick={() => generateMutation.mutate()}
            >
              {t("reports:run.generate")}
            </Button>
          )}
        </Group>
        <Alert variant="light" color="gray" p="xs">
          <Text size="xs" c="dimmed">
            {t("reports:run.emailNote")}
          </Text>
        </Alert>
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
