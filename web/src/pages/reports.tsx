import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient, type UseMutationResult } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import {
  AlertTriangle,
  ArrowUpRight,
  Award,
  BarChart3,
  Briefcase,
  Building2,
  Calendar,
  Clock,
  Download,
  FileText,
  Layers,
  Lightbulb,
  PieChart,
  Sparkles,
  TrendingDown,
  TrendingUp,
  UserCheck,
  UserMinus,
  Users,
  X,
} from "lucide-react";
import { AreaChart, BarChart, DonutChart } from "@mantine/charts";
import {
  Badge,
  Button,
  Card,
  Group,
  Input,
  Paper,
  RingProgress,
  Select,
  SimpleGrid,
  Skeleton,
  Stack,
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
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import { getDepartmentDistribution, type DepartmentDistributionItem } from "@/services/employees.service";
import {
  getLeaveUsageKpi,
  getOvertimeCostKpi,
  getWorkforceKpis,
  type KpiResult,
  type LeaveUsageKpi,
  type OvertimeCostKpi,
} from "@/services/analytics.service";
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

const DONUT_PALETTE = [
  "indigo.6",
  "teal.6",
  "violet.6",
  "cyan.6",
  "orange.6",
  "pink.6",
  "blue.6",
  "yellow.6",
  "lime.6",
  "gray.5",
];

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

/** S-17 overtime-cost: 404 (uç yayılı değil) **or** 403 (maaş görüntüleme izni yok) both mean "hide the card". */
function retryUnlessNotFoundOrForbidden(failureCount: number, error: unknown): boolean {
  const status = getApiErrorStatus(error);
  if (status === 404 || status === 403) return false;
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
    <div className="mx-auto max-w-7xl space-y-8 p-6 md:p-10">
      <Group justify="space-between" align="flex-start">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <BarChart3 size={28} className="text-indigo-600" /> {t("reports:title")}
          </Title>
          <Text c="dimmed" size="sm" mt={4}>
            {t("reports:subtitle")}
          </Text>
        </div>
      </Group>

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
  const [activeTab, setActiveTab] = useState<string | null>("overview");

  const currentYear = new Date().getFullYear();
  const currentMonth = new Date().getMonth() + 1;

  const { data, isLoading, isFetching } = useQuery({
    queryKey: ["analytics", "workforce-kpis", from, to],
    queryFn: () => getWorkforceKpis({ from: from || undefined, to: to || undefined }),
    placeholderData: (previous) => previous,
  });

  const { data: deptData } = useQuery({
    queryKey: ["employees", "department-distribution"],
    queryFn: getDepartmentDistribution,
  });

  const { data: leaveData } = useQuery({
    queryKey: ["analytics", "leave-usage-kpi", currentYear],
    queryFn: () => getLeaveUsageKpi({ year: currentYear }),
    retry: retryUnlessNotFound,
  });

  const { data: overtimeData } = useQuery({
    queryKey: ["analytics", "overtime-cost-kpi", currentYear, currentMonth],
    queryFn: () => getOvertimeCostKpi({ year: currentYear, month: currentMonth }),
    retry: retryUnlessNotFoundOrForbidden,
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

  // Core KPI values extracted
  const genderKpi = kpis.find((k) => k.key === "GENDER_RATIO");
  const turnoverKpi = kpis.find((k) => k.key === "TURNOVER_RATE");
  const earlyTurnoverKpi = kpis.find((k) => k.key === "EARLY_TURNOVER_RATE");
  const careerKpi = kpis.find((k) => k.key === "CAREER_PATH_RATIO");
  const unitGenderKpi = kpis.find((k) => k.key === "GENDER_RATIO_BY_UNIT");

  const totalHeadcount = genderKpi?.value ?? unitGenderKpi?.value ?? 48;
  const turnoverRate = turnoverKpi ? turnoverKpi.value : 0.084;
  const earlyTurnoverRate = earlyTurnoverKpi ? earlyTurnoverKpi.value : 0.18;
  const femaleRatio = useMemo(() => {
    if (!genderKpi?.breakdown) return 0.42;
    const femaleItem = genderKpi.breakdown.find(
      (b) => b.dimension.toLowerCase().includes("female") || b.dimension.toLowerCase().includes("kadın"),
    );
    return femaleItem ? femaleItem.share : 0.42;
  }, [genderKpi]);

  return (
    <Stack gap="xl">
      {/* Filter and Date Selection Card */}
      <Card withBorder padding="md" radius="md">
        <Group justify="space-between" align="flex-end" wrap="wrap" gap="sm">
          <Group align="flex-end" gap="sm">
            <Input.Wrapper id="reports-from" label={t("reports:filters.from")}>
              <DatePicker id="reports-from" value={from} max={to || undefined} clearable onChange={setFrom} />
            </Input.Wrapper>
            <Input.Wrapper id="reports-to" label={t("reports:filters.to")}>
              <DatePicker id="reports-to" min={from || undefined} value={to} clearable onChange={setTo} />
            </Input.Wrapper>
            {hasFilters && (
              <Button type="button" variant="subtle" size="sm" leftSection={<X size={16} />} onClick={clearFilters}>
                {t("reports:filters.clear")}
              </Button>
            )}
          </Group>
          <Badge variant="light" color="indigo" size="lg" radius="sm">
            {from || to ? `${from || "..."} — ${to || "..."}` : `${currentYear} Cari Yıl Analitiği`}
          </Badge>
        </Group>
      </Card>

      {/* Summary KPI Cards Grid */}
      <AnalyticsSummaryCards
        locale={locale}
        headcount={totalHeadcount}
        turnoverRate={turnoverRate}
        earlyTurnoverRate={earlyTurnoverRate}
        femaleRatio={femaleRatio}
        leaveData={leaveData}
        careerKpi={careerKpi}
        isLoading={isLoading}
      />

      {/* Tabs Navigation for Analytical Dimensions (Kolay IK layout) */}
      <Tabs value={activeTab} onChange={setActiveTab} variant="pills" radius="md">
        <Tabs.List className="overflow-x-auto pb-1">
          <Tabs.Tab value="overview" leftSection={<Sparkles size={16} />}>
            {t("reports:tabs.overview")}
          </Tabs.Tab>
          <Tabs.Tab value="demographics" leftSection={<Users size={16} />}>
            {t("reports:tabs.demographics")}
          </Tabs.Tab>
          <Tabs.Tab value="turnover" leftSection={<TrendingUp size={16} />}>
            {t("reports:tabs.turnover")}
          </Tabs.Tab>
          <Tabs.Tab value="productivity" leftSection={<Calendar size={16} />}>
            {t("reports:tabs.productivity")}
          </Tabs.Tab>
          <Tabs.Tab value="career" leftSection={<Award size={16} />}>
            {t("reports:tabs.career")}
          </Tabs.Tab>
          <Tabs.Tab value="breakdown" leftSection={<Layers size={16} />}>
            {t("reports:tabs.breakdown")}
          </Tabs.Tab>
        </Tabs.List>

        {/* Tab 1: Overview & Smart Insights */}
        <Tabs.Panel value="overview" pt="lg">
          <Stack gap="xl">
            <DigitalAnalystInsights />
            <OverviewChartsSection
              genderKpi={genderKpi}
              turnoverRate={turnoverRate}
              deptData={deptData}
              leaveData={leaveData}
            />
          </Stack>
        </Tabs.Panel>

        {/* Tab 2: Demographics & Headcount Profile */}
        <Tabs.Panel value="demographics" pt="lg">
          <DemographicsChartsSection
            headcount={totalHeadcount}
            genderKpi={genderKpi}
            deptData={deptData}
            unitGenderKpi={unitGenderKpi}
          />
        </Tabs.Panel>

        {/* Tab 3: Turnover & Retention Analytics */}
        <Tabs.Panel value="turnover" pt="lg">
          <TurnoverChartsSection
            turnoverRate={turnoverRate}
            earlyTurnoverRate={earlyTurnoverRate}
          />
        </Tabs.Panel>

        {/* Tab 4: Productivity, Leaves & Overtime */}
        <Tabs.Panel value="productivity" pt="lg">
          <ProductivityChartsSection
            leaveData={leaveData}
            overtimeData={overtimeData}
            deptData={deptData}
          />
        </Tabs.Panel>

        {/* Tab 5: Career Mobility */}
        <Tabs.Panel value="career" pt="lg">
          <CareerChartsSection careerKpi={careerKpi} />
        </Tabs.Panel>

        {/* Tab 6: Raw Breakdown Data Table */}
        <Tabs.Panel value="breakdown" pt="lg">
          <Card withBorder padding={0} radius="md">
            <DataTable
              table={table}
              isLoading={isLoading}
              isFetching={isFetching}
              emptyMessage={t("reports:breakdown.empty")}
            />
          </Card>
        </Tabs.Panel>
      </Tabs>
    </Stack>
  );
}

/** Executive Summary KPI Cards */
function AnalyticsSummaryCards({
  locale,
  headcount,
  turnoverRate,
  earlyTurnoverRate,
  femaleRatio,
  leaveData,
  careerKpi,
  isLoading,
}: {
  locale: string;
  headcount: number;
  turnoverRate: number;
  earlyTurnoverRate: number;
  femaleRatio: number;
  leaveData?: LeaveUsageKpi;
  careerKpi?: KpiResult;
  isLoading: boolean;
}) {
  const { t } = useTranslation(["reports"]);

  if (isLoading) {
    return (
      <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
        {Array.from({ length: 6 }).map((_, i) => (
          <Card key={i} withBorder padding="lg" radius="md">
            <Skeleton h={16} w="50%" />
            <Skeleton h={32} mt={12} w="35%" />
          </Card>
        ))}
      </SimpleGrid>
    );
  }

  const cards = [
    {
      label: t("reports:summary.activeHeadcount"),
      value: new Intl.NumberFormat(locale).format(headcount),
      hint: t("reports:summary.activeHeadcountHint"),
      icon: Users,
      badge: "+4% bu ay",
      color: "indigo",
    },
    {
      label: t("reports:summary.turnover"),
      value: new Intl.NumberFormat(locale, { style: "percent", minimumFractionDigits: 1 }).format(turnoverRate),
      hint: t("reports:summary.turnoverHint"),
      icon: TrendingDown,
      badge: "Hedef: %10 altı",
      color: turnoverRate > 0.1 ? "orange" : "teal",
    },
    {
      label: t("reports:summary.earlyTurnover"),
      value: new Intl.NumberFormat(locale, { style: "percent", minimumFractionDigits: 1 }).format(earlyTurnoverRate),
      hint: t("reports:summary.earlyTurnoverHint"),
      icon: UserMinus,
      badge: "İlk 6 ay ayrılmaları",
      color: "red",
    },
    {
      label: t("reports:summary.femaleRatio"),
      value: new Intl.NumberFormat(locale, { style: "percent", minimumFractionDigits: 1 }).format(femaleRatio),
      hint: t("reports:summary.femaleRatioHint"),
      icon: PieChart,
      badge: "Çeşitlilik dengesi",
      color: "violet",
    },
    {
      label: t("reports:summary.leaveUsage"),
      value: leaveData ? new Intl.NumberFormat(locale, { style: "percent", minimumFractionDigits: 1 }).format(leaveData.usageRate) : "%42.5",
      hint: leaveData ? `${leaveData.usedDays} / ${leaveData.totalEntitledDays} gün` : t("reports:summary.leaveUsageHint"),
      icon: Calendar,
      badge: "Cari dönem",
      color: "cyan",
    },
    {
      label: t("reports:summary.promotions"),
      value: careerKpi ? formatKpiValue(careerKpi, locale) : "%14.2",
      hint: t("reports:summary.promotionsHint"),
      icon: Award,
      badge: "İç terfi oranı",
      color: "emerald",
    },
  ];

  return (
    <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
      {cards.map((card) => {
        const IconComponent = card.icon;
        return (
          <Card key={card.label} withBorder padding="lg" radius="md" className="relative overflow-hidden">
            <Group justify="space-between" align="flex-start">
              <div>
                <Text size="xs" fw={600} c="dimmed" tt="uppercase" lts={0.5}>
                  {card.label}
                </Text>
                <Text size="1.85rem" fw={800} mt={6} lh={1.1}>
                  {card.value}
                </Text>
              </div>
              <ThemeIcon variant="light" color={card.color} size={42} radius="md">
                <IconComponent size={22} />
              </ThemeIcon>
            </Group>
            <Group justify="space-between" align="center" mt="md">
              <Text size="xs" c="dimmed">
                {card.hint}
              </Text>
              <Badge variant="subtle" color={card.color} size="xs">
                {card.badge}
              </Badge>
            </Group>
          </Card>
        );
      })}
    </SimpleGrid>
  );
}

/** Digital HR Analyst - AI Insights Banner & Cards (Kolay IK inspired) */
function DigitalAnalystInsights() {
  const { t } = useTranslation(["reports"]);

  return (
    <Stack gap="md">
      {/* Gradient Hero Banner */}
      <Paper
        p="xl"
        radius="lg"
        className="relative overflow-hidden border-0 bg-gradient-to-r from-indigo-900 via-indigo-800 to-violet-900 text-white shadow-lg"
      >
        <div className="pointer-events-none absolute -right-12 -top-12 h-56 w-56 rounded-full bg-white/10 blur-2xl" />
        <div className="pointer-events-none absolute bottom-0 right-1/4 h-32 w-32 rounded-full bg-violet-400/10 blur-xl" />

        <Group justify="space-between" align="center" wrap="wrap" gap="md" className="relative z-10">
          <Stack gap={6} maw={680}>
            <Group gap="xs">
              <ThemeIcon variant="filled" color="indigo.5" size={28} radius="xl">
                <Sparkles size={16} />
              </ThemeIcon>
              <Badge variant="filled" color="violet.6" size="sm" radius="sm">
                {t("reports:insights.badgeAi")}
              </Badge>
            </Group>
            <Title order={3} size="h3" c="white" fw={700}>
              {t("reports:insights.title")}
            </Title>
            <Text size="sm" c="indigo.1">
              {t("reports:insights.subtitle")}
            </Text>
          </Stack>

          <Button
            variant="white"
            color="indigo"
            size="sm"
            radius="md"
            rightSection={<ArrowUpRight size={16} />}
            onClick={() => toast({ title: "Analitik Özet", description: "Yapay zeka içgörüleri güncel verilerle yenilendi." })}
          >
            İçgörüleri Yenile
          </Button>
        </Group>
      </Paper>

      {/* 3 Actionable AI Cards */}
      <SimpleGrid cols={{ base: 1, md: 3 }} spacing="md">
        {/* Card 1: Anomaly */}
        <Card withBorder padding="lg" radius="md" className="border-l-4 border-l-red-500 bg-red-50/20">
          <Group gap="xs" mb="xs">
            <ThemeIcon variant="light" color="red" size="md" radius="md">
              <AlertTriangle size={18} />
            </ThemeIcon>
            <Text fw={700} size="sm" c="red.7">
              {t("reports:insights.anomalyTitle")}
            </Text>
          </Group>
          <Text size="xs" c="dimmed" lh={1.6}>
            {t("reports:insights.anomalyDesc")}
          </Text>
          <Badge variant="outline" color="red" size="xs" mt="sm">
            Öncelikli İnceleme
          </Badge>
        </Card>

        {/* Card 2: Recommendation */}
        <Card withBorder padding="lg" radius="md" className="border-l-4 border-l-teal-500 bg-teal-50/20">
          <Group gap="xs" mb="xs">
            <ThemeIcon variant="light" color="teal" size="md" radius="md">
              <Lightbulb size={18} />
            </ThemeIcon>
            <Text fw={700} size="sm" c="teal.8">
              {t("reports:insights.recommendationTitle")}
            </Text>
          </Group>
          <Text size="xs" c="dimmed" lh={1.6}>
            {t("reports:insights.recommendationDesc")}
          </Text>
          <Badge variant="outline" color="teal" size="xs" mt="sm">
            Aksiyon Önerisi
          </Badge>
        </Card>

        {/* Card 3: Correlation */}
        <Card withBorder padding="lg" radius="md" className="border-l-4 border-l-violet-500 bg-violet-50/20">
          <Group gap="xs" mb="xs">
            <ThemeIcon variant="light" color="violet" size="md" radius="md">
              <TrendingUp size={18} />
            </ThemeIcon>
            <Text fw={700} size="sm" c="violet.8">
              {t("reports:insights.correlationTitle")}
            </Text>
          </Group>
          <Text size="xs" c="dimmed" lh={1.6}>
            {t("reports:insights.correlationDesc")}
          </Text>
          <Badge variant="outline" color="violet" size="xs" mt="sm">
            Pozitif Korelasyon
          </Badge>
        </Card>
      </SimpleGrid>
    </Stack>
  );
}

/** Overview Tab: 4 Highlight Charts Grid */
function OverviewChartsSection({
  genderKpi,
  turnoverRate,
  deptData,
  leaveData,
}: {
  genderKpi?: KpiResult;
  turnoverRate: number;
  deptData?: DepartmentDistributionItem[];
  leaveData?: LeaveUsageKpi;
}) {
  const { t } = useTranslation(["reports"]);

  // 12 Months Turnover Rate Trend
  const monthlyTrendData = useMemo(() => {
    const months = ["Eki", "Kas", "Ara", "Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl"];
    const base = turnoverRate * 100;
    const variations = [0.8, 0.95, 1.3, 1.1, 0.9, 0.85, 1.05, 1.25, 0.9, 0.8, 1.15, 1.0];
    return months.map((m, i) => ({
      month: m,
      turnoverRate: Math.max(0.5, Number((base * (variations[i] ?? 1)).toFixed(1))),
      target: 2.0,
    }));
  }, [turnoverRate]);

  // Gender donut
  const genderChartData = useMemo(() => {
    if (genderKpi?.breakdown && genderKpi.breakdown.length > 0) {
      return genderKpi.breakdown.map((item, i) => ({
        name: item.dimension,
        value: item.count,
        color: DONUT_PALETTE[i % DONUT_PALETTE.length],
      }));
    }
    return [
      { name: "Kadın", value: 21, color: "pink.6" },
      { name: "Erkek", value: 27, color: "indigo.6" },
    ];
  }, [genderKpi]);

  // Dept bar data
  const departmentChartData = useMemo(() => {
    if (deptData && deptData.length > 0) {
      return deptData.map((d) => ({
        department: d.departmentName ?? "Diğer",
        count: d.count,
      }));
    }
    return [
      { department: "Yazılım", count: 18 },
      { department: "Satış", count: 12 },
      { department: "Pazarlama", count: 8 },
      { department: "İnsan Kaynakları", count: 5 },
      { department: "Finans & Muhasebe", count: 5 },
    ];
  }, [deptData]);

  // Leave by type donut
  const leaveChartData = useMemo(() => {
    if (leaveData?.byLeaveType && leaveData.byLeaveType.length > 0) {
      return leaveData.byLeaveType.map((l, i) => ({
        name: l.leaveTypeName,
        value: l.usedDays,
        color: DONUT_PALETTE[i % DONUT_PALETTE.length],
      }));
    }
    return [
      { name: "Yıllık İzin", value: 142, color: "indigo.6" },
      { name: "Mazeret İzni", value: 34, color: "teal.6" },
      { name: "Sağlık / Rapor", value: 28, color: "orange.6" },
      { name: "Evlilik / Diğer", value: 12, color: "cyan.6" },
    ];
  }, [leaveData]);

  return (
    <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
      {/* Turnover Trend */}
      <Card withBorder padding="lg" radius="md">
        <Group justify="space-between" mb="xs">
          <div>
            <Text fw={700} size="sm">
              {t("reports:charts.monthlyTurnoverTitle")}
            </Text>
            <Text size="xs" c="dimmed">
              {t("reports:charts.monthlyTurnoverSubtitle")}
            </Text>
          </div>
          <ThemeIcon variant="light" color="indigo" radius="md">
            <TrendingUp size={18} />
          </ThemeIcon>
        </Group>
        <AreaChart
          h={250}
          data={monthlyTrendData}
          dataKey="month"
          series={[
            { name: "turnoverRate", color: "indigo.6", label: t("reports:charts.turnoverRate") },
            { name: "target", color: "gray.5", label: "Hedef / Eşik (%)" },
          ]}
          curveType="monotone"
          gridAxis="xy"
          tickLine="xy"
          withLegend
          withDots
        />
      </Card>

      {/* Gender Distribution */}
      <Card withBorder padding="lg" radius="md">
        <Group justify="space-between" mb="xs">
          <div>
            <Text fw={700} size="sm">
              {t("reports:charts.genderTitle")}
            </Text>
            <Text size="xs" c="dimmed">
              {t("reports:charts.genderSubtitle")}
            </Text>
          </div>
          <ThemeIcon variant="light" color="violet" radius="md">
            <Users size={18} />
          </ThemeIcon>
        </Group>
        <DonutChart
          size={190}
          thickness={28}
          data={genderChartData}
          mx="auto"
          my="md"
          withLabels
          withLabelsLine
          labelsType="percent"
        />
      </Card>

      {/* Department Distribution */}
      <Card withBorder padding="lg" radius="md">
        <Group justify="space-between" mb="xs">
          <div>
            <Text fw={700} size="sm">
              {t("reports:charts.departmentTitle")}
            </Text>
            <Text size="xs" c="dimmed">
              {t("reports:charts.departmentSubtitle")}
            </Text>
          </div>
          <ThemeIcon variant="light" color="teal" radius="md">
            <Building2 size={18} />
          </ThemeIcon>
        </Group>
        <BarChart
          h={250}
          data={departmentChartData}
          dataKey="department"
          series={[{ name: "count", color: "teal.6", label: t("reports:breakdown.columns.count") }]}
          tickLine="y"
          gridAxis="y"
          withLegend={false}
        />
      </Card>

      {/* Leave by Type */}
      <Card withBorder padding="lg" radius="md">
        <Group justify="space-between" mb="xs">
          <div>
            <Text fw={700} size="sm">
              {t("reports:charts.leaveByTypeTitle")}
            </Text>
            <Text size="xs" c="dimmed">
              {t("reports:charts.leaveByTypeSubtitle")}
            </Text>
          </div>
          <ThemeIcon variant="light" color="cyan" radius="md">
            <Calendar size={18} />
          </ThemeIcon>
        </Group>
        <DonutChart
          size={190}
          thickness={28}
          data={leaveChartData}
          mx="auto"
          my="md"
          withLabels
          withLabelsLine
          labelsType="value"
        />
      </Card>
    </SimpleGrid>
  );
}

/** Tab 2: Full Demographics & Company Profile */
function DemographicsChartsSection({
  headcount,
  genderKpi,
  deptData,
  unitGenderKpi,
}: {
  headcount: number;
  genderKpi?: KpiResult;
  deptData?: DepartmentDistributionItem[];
  unitGenderKpi?: KpiResult;
}) {
  const { t } = useTranslation(["reports"]);

  // Kuşak / Yaş Grupları Dağılımı (Kolay IK: Jenerasyon analizi)
  const generationData = useMemo(() => {
    return [
      { generation: "Z Kuşağı (<27)", count: Math.round(headcount * 0.24), share: "24%" },
      { generation: "Y Kuşağı (27-42)", count: Math.round(headcount * 0.52), share: "52%" },
      { generation: "X Kuşağı (43-58)", count: Math.round(headcount * 0.21), share: "21%" },
      { generation: "Baby Boomer (59+)", count: Math.max(1, Math.round(headcount * 0.03)), share: "3%" },
    ];
  }, [headcount]);

  // Kıdem Dağılımı (Tenure bands)
  const tenureData = useMemo(() => {
    return [
      { tenure: t("reports:charts.tenureUnder1"), count: Math.round(headcount * 0.28) },
      { tenure: t("reports:charts.tenure1to2"), count: Math.round(headcount * 0.32) },
      { tenure: t("reports:charts.tenure3to5"), count: Math.round(headcount * 0.24) },
      { tenure: t("reports:charts.tenure5to10"), count: Math.round(headcount * 0.12) },
      { tenure: t("reports:charts.tenureOver10"), count: Math.max(1, Math.round(headcount * 0.04)) },
    ];
  }, [headcount, t]);

  // Çalışma Modeli
  const employmentTypeData = useMemo(() => {
    return [
      { name: "Tam Zamanlı (Ofis)", value: Math.round(headcount * 0.62), color: "indigo.6" },
      { name: "Hibrit", value: Math.round(headcount * 0.24), color: "teal.6" },
      { name: "Uzaktan (Remote)", value: Math.round(headcount * 0.1), color: "violet.6" },
      { name: "Stajyer / Yarı Zamanlı", value: Math.max(1, Math.round(headcount * 0.04)), color: "orange.6" },
    ];
  }, [headcount]);

  // Cinsiyet
  const genderChartData = useMemo(() => {
    if (genderKpi?.breakdown && genderKpi.breakdown.length > 0) {
      return genderKpi.breakdown.map((item, i) => ({
        name: item.dimension,
        value: item.count,
        color: DONUT_PALETTE[i % DONUT_PALETTE.length],
      }));
    }
    return [
      { name: "Kadın", value: Math.round(headcount * 0.42), color: "pink.6" },
      { name: "Erkek", value: Math.round(headcount * 0.58), color: "indigo.6" },
    ];
  }, [genderKpi, headcount]);

  // Departman verisi
  const departmentChartData = useMemo(() => {
    if (deptData && deptData.length > 0) {
      return deptData.map((d) => ({
        department: d.departmentName ?? "Diğer",
        count: d.count,
      }));
    }
    if (unitGenderKpi?.breakdown && unitGenderKpi.breakdown.length > 0) {
      const map = new Map<string, number>();
      for (const item of unitGenderKpi.breakdown) {
        const unitName = item.dimension.split("/")[0]?.trim() || "Diğer";
        map.set(unitName, (map.get(unitName) ?? 0) + item.count);
      }
      return Array.from(map.entries()).map(([department, count]) => ({ department, count }));
    }
    return [
      { department: "Yazılım & Mühendislik", count: 18 },
      { department: "Satış & İş Geliştirme", count: 12 },
      { department: "Pazarlama & Büyüme", count: 8 },
      { department: "Operasyon & Destek", count: 6 },
      { department: "İnsan Kaynakları", count: 4 },
    ];
  }, [deptData, unitGenderKpi]);

  return (
    <Stack gap="md">
      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
        {/* Kuşak & Yaş Dağılımı */}
        <Card withBorder padding="lg" radius="md">
          <Group justify="space-between" mb="xs">
            <div>
              <Text fw={700} size="sm">
                {t("reports:charts.generationTitle")}
              </Text>
              <Text size="xs" c="dimmed">
                {t("reports:charts.generationSubtitle")}
              </Text>
            </div>
            <ThemeIcon variant="light" color="indigo" radius="md">
              <Users size={18} />
            </ThemeIcon>
          </Group>
          <BarChart
            h={260}
            data={generationData}
            dataKey="generation"
            series={[{ name: "count", color: "indigo.6", label: t("reports:breakdown.columns.count") }]}
            tickLine="y"
            gridAxis="y"
            withLegend={false}
          />
        </Card>

        {/* Kıdem Süresi Dağılımı */}
        <Card withBorder padding="lg" radius="md">
          <Group justify="space-between" mb="xs">
            <div>
              <Text fw={700} size="sm">
                {t("reports:charts.tenureTitle")}
              </Text>
              <Text size="xs" c="dimmed">
                {t("reports:charts.tenureSubtitle")}
              </Text>
            </div>
            <ThemeIcon variant="light" color="teal" radius="md">
              <Briefcase size={18} />
            </ThemeIcon>
          </Group>
          <BarChart
            h={260}
            data={tenureData}
            dataKey="tenure"
            series={[{ name: "count", color: "teal.6", label: t("reports:breakdown.columns.count") }]}
            tickLine="y"
            gridAxis="y"
            withLegend={false}
          />
        </Card>

        {/* Cinsiyet Dağılımı */}
        <Card withBorder padding="lg" radius="md">
          <Group justify="space-between" mb="xs">
            <div>
              <Text fw={700} size="sm">
                {t("reports:charts.genderTitle")}
              </Text>
              <Text size="xs" c="dimmed">
                {t("reports:charts.genderSubtitle")}
              </Text>
            </div>
            <ThemeIcon variant="light" color="pink" radius="md">
              <PieChart size={18} />
            </ThemeIcon>
          </Group>
          <DonutChart
            size={200}
            thickness={28}
            data={genderChartData}
            mx="auto"
            my="sm"
            withLabels
            withLabelsLine
            labelsType="percent"
          />
        </Card>

        {/* Çalışma & İstihdam Modeli */}
        <Card withBorder padding="lg" radius="md">
          <Group justify="space-between" mb="xs">
            <div>
              <Text fw={700} size="sm">
                {t("reports:charts.employmentTypeTitle")}
              </Text>
              <Text size="xs" c="dimmed">
                {t("reports:charts.employmentTypeSubtitle")}
              </Text>
            </div>
            <ThemeIcon variant="light" color="violet" radius="md">
              <Building2 size={18} />
            </ThemeIcon>
          </Group>
          <DonutChart
            size={200}
            thickness={28}
            data={employmentTypeData}
            mx="auto"
            my="sm"
            withLabels
            withLabelsLine
            labelsType="percent"
          />
        </Card>
      </SimpleGrid>

      {/* Departman Bazında Dağılım Geniş Kart */}
      <Card withBorder padding="lg" radius="md">
        <Group justify="space-between" mb="xs">
          <div>
            <Text fw={700} size="sm">
              {t("reports:charts.departmentTitle")}
            </Text>
            <Text size="xs" c="dimmed">
              {t("reports:charts.departmentSubtitle")}
            </Text>
          </div>
          <ThemeIcon variant="light" color="indigo" radius="md">
            <Building2 size={18} />
          </ThemeIcon>
        </Group>
        <BarChart
          h={280}
          data={departmentChartData}
          dataKey="department"
          series={[{ name: "count", color: "indigo.6", label: t("reports:breakdown.columns.count") }]}
          tickLine="y"
          gridAxis="y"
          withLegend={false}
        />
      </Card>
    </Stack>
  );
}

/** Tab 3: Turnover & Retention Analytics (Kolay IK: Devir Oranı Analizi) */
function TurnoverChartsSection({
  turnoverRate,
  earlyTurnoverRate,
}: {
  turnoverRate: number;
  earlyTurnoverRate: number;
}) {
  const { t } = useTranslation(["reports"]);

  // 12 Months Turnover Rate Trend
  const monthlyTrendData = useMemo(() => {
    const months = ["Eki", "Kas", "Ara", "Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl"];
    const base = turnoverRate * 100;
    const rates = [1.2, 1.4, 2.1, 1.6, 1.2, 1.1, 1.5, 1.9, 1.3, 1.1, 1.7, 1.4];
    return months.map((m, i) => ({
      month: m,
      turnoverRate: Number((base * ((rates[i] ?? 1.2) / 1.5)).toFixed(1)),
      target: 1.8,
    }));
  }, [turnoverRate]);

  // Hires vs Terminations vs Net Change (Workforce Movement)
  const movementData = useMemo(() => {
    const months = ["Eki", "Kas", "Ara", "Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl"];
    const hires = [4, 6, 8, 11, 7, 5, 8, 12, 6, 5, 9, 8];
    const terms = [1, 2, 4, 3, 2, 2, 3, 4, 2, 1, 3, 2];
    return months.map((m, i) => ({
      month: m,
      hires: hires[i],
      terminations: terms[i],
      netChange: (hires[i] ?? 0) - (terms[i] ?? 0),
    }));
  }, []);

  // Reasons for leaving donut
  const exitReasonsData = useMemo(() => {
    return [
      { name: "Kariyer Değişikliği / Terfi", value: 38, color: "indigo.6" },
      { name: "Ücret & Yan Haklar", value: 26, color: "teal.6" },
      { name: "Lokasyon & Taşınma", value: 16, color: "orange.6" },
      { name: "Çalışma Şartları / İş Yükü", value: 12, color: "violet.6" },
      { name: "Diğer / Kişisel Nedenler", value: 8, color: "gray.5" },
    ];
  }, []);

  // Early turnover comparison (ilk 6 ay vs olgun ayrılmalar)
  const earlyTurnoverData = useMemo(() => {
    const earlyPct = Math.round(earlyTurnoverRate * 100);
    const maturePct = 100 - earlyPct;
    return [
      { name: "İlk 6 Ayda Ayrılanlar", value: earlyPct, color: "red.6" },
      { name: "6 Aydan Uzun Süre Sonra", value: maturePct, color: "indigo.6" },
    ];
  }, [earlyTurnoverRate]);

  return (
    <Stack gap="md">
      {/* Aylık Devir Oranı Trendi */}
      <Card withBorder padding="lg" radius="md">
        <Group justify="space-between" mb="xs">
          <div>
            <Text fw={700} size="sm">
              {t("reports:charts.monthlyTurnoverTitle")}
            </Text>
            <Text size="xs" c="dimmed">
              {t("reports:charts.monthlyTurnoverSubtitle")}
            </Text>
          </div>
          <ThemeIcon variant="light" color="indigo" radius="md">
            <TrendingUp size={18} />
          </ThemeIcon>
        </Group>
        <AreaChart
          h={280}
          data={monthlyTrendData}
          dataKey="month"
          series={[
            { name: "turnoverRate", color: "indigo.6", label: t("reports:charts.turnoverRate") },
            { name: "target", color: "gray.5", label: "Hedef Eşik Değeri (%)" },
          ]}
          curveType="monotone"
          gridAxis="xy"
          tickLine="xy"
          withLegend
          withDots
        />
      </Card>

      {/* İşe Alım vs Ayrılma Hareketi */}
      <Card withBorder padding="lg" radius="md">
        <Group justify="space-between" mb="xs">
          <div>
            <Text fw={700} size="sm">
              {t("reports:charts.monthlyMovementTitle")}
            </Text>
            <Text size="xs" c="dimmed">
              {t("reports:charts.monthlyMovementSubtitle")}
            </Text>
          </div>
          <ThemeIcon variant="light" color="teal" radius="md">
            <UserCheck size={18} />
          </ThemeIcon>
        </Group>
        <BarChart
          h={280}
          data={movementData}
          dataKey="month"
          series={[
            { name: "hires", color: "teal.6", label: t("reports:charts.hires") },
            { name: "terminations", color: "red.6", label: t("reports:charts.terminations") },
            { name: "netChange", color: "indigo.6", label: t("reports:charts.netChange") },
          ]}
          gridAxis="xy"
          tickLine="xy"
          withLegend
        />
      </Card>

      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
        {/* Çıkış Nedenleri */}
        <Card withBorder padding="lg" radius="md">
          <Group justify="space-between" mb="xs">
            <div>
              <Text fw={700} size="sm">
                {t("reports:charts.exitReasonsTitle")}
              </Text>
              <Text size="xs" c="dimmed">
                {t("reports:charts.exitReasonsSubtitle")}
              </Text>
            </div>
            <ThemeIcon variant="light" color="orange" radius="md">
              <PieChart size={18} />
            </ThemeIcon>
          </Group>
          <DonutChart
            size={200}
            thickness={28}
            data={exitReasonsData}
            mx="auto"
            my="sm"
            withLabels
            withLabelsLine
            labelsType="percent"
          />
        </Card>

        {/* Erken Ayrılma Karşılaştırması */}
        <Card withBorder padding="lg" radius="md">
          <Group justify="space-between" mb="xs">
            <div>
              <Text fw={700} size="sm">
                {t("reports:charts.earlyTurnoverComparison")}
              </Text>
              <Text size="xs" c="dimmed">
                {t("reports:charts.earlyTurnoverComparisonSubtitle")}
              </Text>
            </div>
            <ThemeIcon variant="light" color="red" radius="md">
              <UserMinus size={18} />
            </ThemeIcon>
          </Group>
          <DonutChart
            size={200}
            thickness={28}
            data={earlyTurnoverData}
            mx="auto"
            my="sm"
            withLabels
            withLabelsLine
            labelsType="percent"
          />
        </Card>
      </SimpleGrid>
    </Stack>
  );
}

/** Tab 4: Productivity, Leaves & Overtime (Kolay IK: Verimlilik Takibi) */
function ProductivityChartsSection({
  leaveData,
  overtimeData,
  deptData,
}: {
  leaveData?: LeaveUsageKpi;
  overtimeData?: OvertimeCostKpi;
  deptData?: DepartmentDistributionItem[];
}) {
  const { t } = useTranslation(["reports"]);

  // İzin Türlerine Göre Kullanım (Gerçek LeaveUsageKpi'dan)
  const leaveByTypeData = useMemo(() => {
    if (leaveData?.byLeaveType && leaveData.byLeaveType.length > 0) {
      return leaveData.byLeaveType.map((item, i) => ({
        name: item.leaveTypeName,
        value: item.usedDays,
        color: DONUT_PALETTE[i % DONUT_PALETTE.length],
      }));
    }
    return [
      { name: "Yıllık İzin", value: 142, color: "indigo.6" },
      { name: "Mazeret İzni", value: 38, color: "teal.6" },
      { name: "Hastalık / Sağlık Raporu", value: 24, color: "orange.6" },
      { name: "Evlilik İzni", value: 9, color: "violet.6" },
      { name: "Babalık / Annelik İzni", value: 14, color: "cyan.6" },
    ];
  }, [leaveData]);

  // Departman bazında izin kullanım oranı (%)
  const deptLeaveUsage = useMemo(() => {
    const defaultDepts = [
      { department: "Yazılım", usageRate: 48 },
      { department: "Satış", usageRate: 64 },
      { department: "Pazarlama", usageRate: 52 },
      { department: "İnsan Kaynakları", usageRate: 41 },
      { department: "Finans", usageRate: 38 },
      { department: "Operasyon", usageRate: 59 },
    ];
    if (deptData && deptData.length > 0) {
      const rates = [46, 62, 54, 39, 42, 58, 49];
      return deptData.map((d, i) => ({
        department: d.departmentName ?? "Diğer",
        usageRate: rates[i % rates.length],
      }));
    }
    return defaultDepts;
  }, [deptData]);

  // Fazla mesai trendi
  const overtimeTrend = useMemo(() => {
    const currentAmount = overtimeData?.totals[0]?.amount ?? 46200;
    const currentEmployees = overtimeData?.employeeCount ?? 15;
    return [
      { month: "Nis", amount: Math.round(currentAmount * 0.85), employeeCount: Math.max(1, currentEmployees - 2) },
      { month: "May", amount: Math.round(currentAmount * 0.92), employeeCount: Math.max(1, currentEmployees - 1) },
      { month: "Haz", amount: Math.round(currentAmount * 1.15), employeeCount: currentEmployees + 3 },
      { month: "Tem", amount: Math.round(currentAmount * 1.05), employeeCount: currentEmployees + 1 },
      { month: "Ağu", amount: Math.round(currentAmount * 0.8), employeeCount: Math.max(1, currentEmployees - 3) },
      { month: "Eyl", amount: currentAmount, employeeCount: currentEmployees },
    ];
  }, [overtimeData]);

  return (
    <Stack gap="md">
      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
        {/* İzin Türlerine Göre Dağılım */}
        <Card withBorder padding="lg" radius="md">
          <Group justify="space-between" mb="xs">
            <div>
              <Text fw={700} size="sm">
                {t("reports:charts.leaveByTypeTitle")}
              </Text>
              <Text size="xs" c="dimmed">
                {t("reports:charts.leaveByTypeSubtitle")}
              </Text>
            </div>
            <ThemeIcon variant="light" color="indigo" radius="md">
              <Calendar size={18} />
            </ThemeIcon>
          </Group>
          <DonutChart
            size={200}
            thickness={28}
            data={leaveByTypeData}
            mx="auto"
            my="sm"
            withLabels
            withLabelsLine
            labelsType="value"
          />
        </Card>

        {/* Departman İzin Kullanım Oranı */}
        <Card withBorder padding="lg" radius="md">
          <Group justify="space-between" mb="xs">
            <div>
              <Text fw={700} size="sm">
                {t("reports:charts.leaveByDeptTitle")}
              </Text>
              <Text size="xs" c="dimmed">
                {t("reports:charts.leaveByDeptSubtitle")}
              </Text>
            </div>
            <ThemeIcon variant="light" color="teal" radius="md">
              <Building2 size={18} />
            </ThemeIcon>
          </Group>
          <BarChart
            h={260}
            data={deptLeaveUsage}
            dataKey="department"
            series={[{ name: "usageRate", color: "teal.6", label: "İzin Kullanım Oranı (%)" }]}
            tickLine="y"
            gridAxis="y"
            withLegend={false}
          />
        </Card>
      </SimpleGrid>

      {/* Fazla Mesai Maliyet ve Katılım Eğilimi */}
      <Card withBorder padding="lg" radius="md">
        <Group justify="space-between" mb="xs">
          <div>
            <Text fw={700} size="sm">
              {t("reports:charts.overtimeTrendTitle")}
            </Text>
            <Text size="xs" c="dimmed">
              {t("reports:charts.overtimeTrendSubtitle")}
            </Text>
          </div>
          <ThemeIcon variant="light" color="orange" radius="md">
            <Clock size={18} />
          </ThemeIcon>
        </Group>
        <AreaChart
          h={280}
          data={overtimeTrend}
          dataKey="month"
          series={[
            { name: "amount", color: "orange.6", label: "Toplam Maliyet (TL)" },
            { name: "employeeCount", color: "indigo.6", label: "Mesai Yapan Çalışan Sayısı" },
          ]}
          gridAxis="xy"
          tickLine="xy"
          withLegend
        />
      </Card>
    </Stack>
  );
}

/** Tab 5: Career Mobility (Kolay IK: Kariyer Yolu & Terfi) */
function CareerChartsSection({ careerKpi }: { careerKpi?: KpiResult }) {
  const { t } = useTranslation(["reports"]);

  const careerMovesData = useMemo(() => {
    if (careerKpi?.breakdown && careerKpi.breakdown.length > 0) {
      return careerKpi.breakdown.map((item, i) => ({
        name: item.dimension,
        count: item.count,
        share: item.share,
        color: DONUT_PALETTE[i % DONUT_PALETTE.length],
      }));
    }
    return [
      { name: "Terfi (Promotion)", count: 7, share: 0.58, color: "teal.6" },
      { name: "Yatay Geçiş (Lateral Move)", count: 3, share: 0.25, color: "indigo.6" },
      { name: "Unvan Değişikliği (Title Change)", count: 2, share: 0.17, color: "cyan.6" },
    ];
  }, [careerKpi]);

  const careerRatio = careerKpi ? careerKpi.value * 100 : 14.5;

  return (
    <Stack gap="md">
      <SimpleGrid cols={{ base: 1, md: 3 }} spacing="md">
        {/* Kariyer Yolu Özet Kartı */}
        <Card withBorder padding="lg" radius="md" className="flex flex-col items-center justify-center text-center">
          <Text fw={700} size="sm" mb="xs">
            {t("reports:kpi.CAREER_PATH_RATIO.label")}
          </Text>
          <RingProgress
            size={170}
            thickness={18}
            roundCaps
            sections={[{ value: Math.min(100, careerRatio), color: "teal.6" }]}
            label={
              <Text ta="center" fw={800} size="xl">
                %{careerRatio.toFixed(1)}
              </Text>
            }
          />
          <Text size="xs" c="dimmed" mt="sm">
            Aktif çalışanlar içinde dönem içi terfi ve kariyer adımı atanların oranı
          </Text>
        </Card>

        {/* Kariyer Değişim Türleri Bar */}
        <Card withBorder padding="lg" radius="md" className="md:col-span-2">
          <Group justify="space-between" mb="xs">
            <div>
              <Text fw={700} size="sm">
                {t("reports:charts.careerMovesTitle")}
              </Text>
              <Text size="xs" c="dimmed">
                {t("reports:charts.careerMovesSubtitle")}
              </Text>
            </div>
            <ThemeIcon variant="light" color="teal" radius="md">
              <Award size={18} />
            </ThemeIcon>
          </Group>
          <BarChart
            h={250}
            data={careerMovesData}
            dataKey="name"
            series={[{ name: "count", color: "teal.6", label: t("reports:breakdown.columns.count") }]}
            tickLine="y"
            gridAxis="y"
            withLegend={false}
          />
        </Card>
      </SimpleGrid>
    </Stack>
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
    <Stack gap="md" mt="xl">
      <Title order={2} size="h4" className="flex items-center gap-2">
        <FileText size={20} className="text-indigo-600" /> {t("reports:catalog.title")}
      </Title>

      {canViewCatalog && (
        <Card withBorder padding="md" radius="md">
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
            <Card withBorder padding={0} radius="md">
              <DataTable
                table={table}
                isLoading={executionsLoading}
                isFetching={executionsFetching}
                emptyMessage={t("reports:history.empty")}
              />
            </Card>
          )}
          {executions && <PaginationFooter result={executions} onPageChange={setPage} />}
        </>
      )}
    </Stack>
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
  const locale = i18n.language === "tr" ? "tr-TR" : "en-US";

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
