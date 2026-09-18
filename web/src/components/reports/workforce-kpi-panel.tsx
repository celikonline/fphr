import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Award, Calendar, Clock, TrendingDown, UserMinus, Users, X } from "lucide-react";
import { BarChart, DonutChart } from "@mantine/charts";
import { Alert, Button, Card, Group, Input, SimpleGrid, Skeleton, Stack, Text, ThemeIcon, Title } from "@mantine/core";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { DatePicker } from "@/components/ui/date-picker";
import { getApiErrorStatus } from "@/lib/api-error";
import { formatMoney } from "@/lib/money";
import { getDepartmentDistribution } from "@/services/employees.service";
import {
  getLeaveUsageKpi,
  getOvertimeCostKpi,
  getWorkforceKpis,
  type KpiBreakdown,
  type KpiResult,
  type LeaveUsageKpi,
  type OvertimeCostKpi,
} from "@/services/analytics.service";

/**
 * Identity colors follow the entity, never its rank (dataviz: fixed categorical order). Gender keys are the C# enum
 * names the API sends; anything else falls back to the neutral slot so an unexpected value never borrows a hue.
 */
const GENDER_COLOR: Record<string, string> = {
  Female: "pink.6",
  Male: "indigo.6",
  Other: "teal.6",
  Unspecified: "gray.5",
};
const GENDER_ORDER = ["Female", "Male", "Other", "Unspecified"];
const CAREER_ORDER = ["Promotion", "LateralMove", "TitleChange", "Demotion"];
/** Single-hue (magnitude) charts. */
const MAGNITUDE_COLOR = "indigo.6";

interface BreakdownRow {
  id: string;
  kpiLabel: string;
  dimension: string;
  count?: number;
  share?: number;
  isSuppressed: boolean;
}

/** S-15/S-17 endpoints aren't deployed everywhere (faz-10 R0): 404 = hide; 403 on overtime = no salary permission = hide. */
function retryUnlessHidden(failureCount: number, error: unknown): boolean {
  const status = getApiErrorStatus(error);
  if (status === 404 || status === 403) return false;
  return failureCount < 1;
}

function percent(value: number, locale: string): string {
  return new Intl.NumberFormat(locale, { style: "percent", minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(value);
}

function integer(value: number, locale: string): string {
  return new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(value);
}

/**
 * İK göstergeleri (roadmap Faz 7 #99): only what the backend actually computes - workforce KPIs, annual leave usage,
 * overtime cost, unit headcount. No placeholder series, no synthetic trends: a card or chart without data says so.
 */
export function WorkforceKpiPanel() {
  const { t, i18n } = useTranslation(["reports"]);
  const locale = i18n.language === "tr" ? "tr-TR" : i18n.language === "de" ? "de-DE" : "en-US";
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const now = useMemo(() => new Date(), []);
  const currentYear = now.getFullYear();
  const currentMonth = now.getMonth() + 1;

  const kpisQuery = useQuery({
    queryKey: ["analytics", "workforce-kpis", from, to],
    queryFn: () => getWorkforceKpis({ from: from || undefined, to: to || undefined }),
    placeholderData: (previous) => previous,
  });
  const deptQuery = useQuery({ queryKey: ["employees", "department-distribution"], queryFn: getDepartmentDistribution, retry: retryUnlessHidden });
  const leaveQuery = useQuery({
    queryKey: ["analytics", "leave-usage-kpi", currentYear],
    queryFn: () => getLeaveUsageKpi({ year: currentYear }),
    retry: retryUnlessHidden,
  });
  const overtimeQuery = useQuery({
    queryKey: ["analytics", "overtime-cost-kpi", currentYear, currentMonth],
    queryFn: () => getOvertimeCostKpi({ year: currentYear, month: currentMonth }),
    retry: retryUnlessHidden,
  });

  const kpis = useMemo(() => kpisQuery.data ?? [], [kpisQuery.data]);
  const byKey = useMemo(() => new Map(kpis.map((k) => [k.key, k])), [kpis]);
  const genderKpi = byKey.get("GENDER_RATIO");
  const unitGenderKpi = byKey.get("GENDER_RATIO_BY_UNIT");
  const isUnitScoped = !kpisQuery.isLoading && kpis.length > 0 && !genderKpi && Boolean(unitGenderKpi);

  // G-10-21: a suppressed cell's count never reached the client (WhenWritingNull) - the bar chart only draws
  // rows it actually has a number for; suppressed ones are surfaced as a count below it instead of a bar.
  const careerBreakdown = byKey.get("CAREER_PATH_RATIO")?.breakdown ?? [];
  const careerVisible = careerBreakdown.filter((b) => !b.isSuppressed && (b.count ?? 0) > 0);
  const careerSuppressedCount = careerBreakdown.filter((b) => b.isSuppressed).length;

  const dimensionLabel = (dimension: string) => t(`reports:kpi.dimensions.${dimension}`, { defaultValue: dimension });
  const kpiLabel = (kpi: KpiResult) => t(`reports:kpi.${kpi.key}.label`, { defaultValue: kpi.label });

  const breakdownRows: BreakdownRow[] = kpis.flatMap((kpi) =>
    kpi.breakdown.map((b, index) => ({
      id: `${kpi.key}-${index}`,
      kpiLabel: kpiLabel(kpi),
      dimension: b.dimension.includes(" / ")
        ? b.dimension
            .split(" / ")
            .map((part, i) => (i === 0 ? dimensionLabel(part) : dimensionLabel(part)))
            .join(" / ")
        : dimensionLabel(b.dimension),
      count: b.count,
      share: b.share,
      isSuppressed: b.isSuppressed,
    })),
  );

  const columns: ColumnDef<BreakdownRow>[] = [
    { id: "kpiLabel", accessorKey: "kpiLabel", header: t("reports:kpis.breakdown.columns.kpi"), meta: { label: t("reports:kpis.breakdown.columns.kpi") } },
    { id: "dimension", accessorKey: "dimension", header: t("reports:kpis.breakdown.columns.dimension"), meta: { label: t("reports:kpis.breakdown.columns.dimension") } },
    {
      id: "count",
      accessorKey: "count",
      header: t("reports:kpis.breakdown.columns.count"),
      meta: { label: t("reports:kpis.breakdown.columns.count"), headerClassName: "w-28" },
      // G-10-21: a suppressed row's count never reached the client at all (WhenWritingNull) - show that, not "—".
      cell: ({ row }) =>
        row.original.isSuppressed ? (
          <Text size="xs" c="dimmed" fs="italic">
            {t("reports:kpis.charts.suppressed")}
          </Text>
        ) : (
          integer(row.original.count ?? 0, locale)
        ),
    },
    {
      id: "share",
      accessorKey: "share",
      header: t("reports:kpis.breakdown.columns.share"),
      meta: { label: t("reports:kpis.breakdown.columns.share"), headerClassName: "w-28" },
      cell: ({ row }) => (row.original.isSuppressed ? "—" : percent(row.original.share ?? 0, locale)),
    },
  ];
  const table = useDataTable({ columns, data: breakdownRows, getRowId: (r) => r.id, pageSize: 10 });
  const hasFilters = from !== "" || to !== "";

  return (
    <Stack gap="lg">
      <div>
        <Title order={2} size="h4">
          {t("reports:kpis.title")}
        </Title>
        <Text c="dimmed" size="sm" mt={2}>
          {t("reports:kpis.subtitle")}
        </Text>
      </div>

      <Card withBorder padding="md" radius="md">
        <Group align="flex-end" gap="sm" wrap="wrap">
          <Input.Wrapper id="kpi-from" label={t("reports:kpis.filters.from")}>
            <DatePicker id="kpi-from" value={from} max={to || undefined} clearable onChange={setFrom} />
          </Input.Wrapper>
          <Input.Wrapper id="kpi-to" label={t("reports:kpis.filters.to")}>
            <DatePicker id="kpi-to" min={from || undefined} value={to} clearable onChange={setTo} />
          </Input.Wrapper>
          {hasFilters ? (
            <Button type="button" variant="subtle" size="sm" leftSection={<X size={16} />} onClick={() => (setFrom(""), setTo(""))}>
              {t("reports:kpis.filters.clear")}
            </Button>
          ) : (
            <Text size="sm" c="dimmed" pb={6}>
              {t("reports:kpis.filters.currentYear")} · {currentYear}
            </Text>
          )}
        </Group>
      </Card>

      {isUnitScoped && (
        <Alert variant="light" color="blue">
          {t("reports:kpis.scopedNote")}
        </Alert>
      )}

      <KpiCards
        locale={locale}
        isLoading={kpisQuery.isLoading}
        kpis={byKey}
        leave={leaveQuery.data}
        overtime={overtimeQuery.data}
        year={currentYear}
        monthLabel={new Intl.DateTimeFormat(i18n.language, { month: "long", year: "numeric" }).format(now)}
      />

      <SimpleGrid cols={{ base: 1, md: 2 }} spacing="md">
        <ChartCard title={t("reports:kpis.charts.gender")}>
          {genderKpi && genderKpi.breakdown.length > 0 ? (
            <GenderDonut breakdown={genderKpi.breakdown} locale={locale} dimensionLabel={dimensionLabel} />
          ) : (
            <EmptyChart />
          )}
        </ChartCard>

        <ChartCard title={t("reports:kpis.charts.department")}>
          {deptQuery.data && deptQuery.data.length > 0 ? (
            <BarChart
              h={Math.max(160, deptQuery.data.length * 32)}
              orientation="vertical"
              data={deptQuery.data.map((d) => ({ name: d.departmentName ?? dimensionLabel("Unassigned"), count: d.count }))}
              dataKey="name"
              series={[{ name: "count", color: MAGNITUDE_COLOR, label: t("reports:kpis.charts.count") }]}
              withBarValueLabel
              gridAxis="x"
              tickLine="none"
              barProps={{ radius: 4 }}
            />
          ) : (
            <EmptyChart />
          )}
        </ChartCard>

        <ChartCard title={t("reports:kpis.charts.leaveByType", { year: currentYear })}>
          {leaveQuery.data && leaveQuery.data.byLeaveType.length > 0 ? (
            <BarChart
              h={Math.max(160, leaveQuery.data.byLeaveType.length * 32)}
              orientation="vertical"
              data={leaveQuery.data.byLeaveType.map((l) => ({ name: l.leaveTypeName, days: l.usedDays }))}
              dataKey="name"
              series={[{ name: "days", color: MAGNITUDE_COLOR, label: t("reports:kpis.charts.days") }]}
              withBarValueLabel
              gridAxis="x"
              tickLine="none"
              barProps={{ radius: 4 }}
            />
          ) : (
            <EmptyChart />
          )}
        </ChartCard>

        <ChartCard title={t("reports:kpis.charts.careerMoves")}>
          {careerVisible.length === 0 && careerSuppressedCount === 0 ? (
            <EmptyChart />
          ) : (
            <Stack gap="xs">
              {careerVisible.length > 0 && (
                <BarChart
                  h={180}
                  orientation="vertical"
                  data={[...careerVisible]
                    .sort((a, b) => CAREER_ORDER.indexOf(a.dimension) - CAREER_ORDER.indexOf(b.dimension))
                    .map((b) => ({ name: dimensionLabel(b.dimension), count: b.count ?? 0 }))}
                  dataKey="name"
                  series={[{ name: "count", color: MAGNITUDE_COLOR, label: t("reports:kpis.charts.count") }]}
                  withBarValueLabel
                  gridAxis="x"
                  tickLine="none"
                  barProps={{ radius: 4 }}
                />
              )}
              {careerSuppressedCount > 0 && (
                <Text size="xs" c="dimmed">
                  {t("reports:kpis.charts.suppressedCount", { count: careerSuppressedCount })}
                </Text>
              )}
            </Stack>
          )}
        </ChartCard>
      </SimpleGrid>

      <Stack gap="xs">
        <Title order={3} size="h5">
          {t("reports:kpis.breakdown.title")}
        </Title>
        <Card withBorder padding={0} radius="md">
          <DataTable table={table} isLoading={kpisQuery.isLoading} isFetching={kpisQuery.isFetching} emptyMessage={t("reports:kpis.breakdown.empty")} />
        </Card>
      </Stack>
    </Stack>
  );
}

function KpiCards({
  locale,
  isLoading,
  kpis,
  leave,
  overtime,
  year,
  monthLabel,
}: {
  locale: string;
  isLoading: boolean;
  kpis: Map<string, KpiResult>;
  leave?: LeaveUsageKpi;
  overtime?: OvertimeCostKpi;
  year: number;
  monthLabel: string;
}) {
  const { t, i18n } = useTranslation(["reports"]);

  if (isLoading) {
    return (
      <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
        {Array.from({ length: 3 }).map((_, i) => (
          <Card key={i} withBorder padding="lg" radius="md">
            <Skeleton h={14} w="50%" />
            <Skeleton h={30} mt={12} w="35%" />
          </Card>
        ))}
      </SimpleGrid>
    );
  }

  const gender = kpis.get("GENDER_RATIO");
  const turnover = kpis.get("TURNOVER_RATE");
  const early = kpis.get("EARLY_TURNOVER_RATE");
  const career = kpis.get("CAREER_PATH_RATIO");

  const cards: { key: string; label: string; value: string; hint?: string; icon: typeof Users; color: string }[] = [];
  if (gender) cards.push({ key: "headcount", label: t("reports:kpis.cards.headcount"), value: integer(gender.value, locale), icon: Users, color: "indigo" });
  if (turnover) {
    cards.push({ key: "turnover", label: t("reports:kpis.cards.turnover"), value: percent(turnover.value, locale), hint: t("reports:kpis.cards.turnoverHint"), icon: TrendingDown, color: "orange" });
  }
  if (early) {
    cards.push({ key: "early", label: t("reports:kpis.cards.earlyTurnover"), value: percent(early.value, locale), hint: t("reports:kpis.cards.earlyTurnoverHint"), icon: UserMinus, color: "red" });
  }
  if (career) {
    cards.push({ key: "career", label: t("reports:kpis.cards.careerPath"), value: percent(career.value, locale), hint: t("reports:kpis.cards.careerPathHint"), icon: Award, color: "teal" });
  }
  if (leave) {
    cards.push({
      key: "leave",
      label: t("reports:kpis.cards.leaveUsage"),
      value: percent(leave.usageRate, locale),
      hint: t("reports:kpis.cards.leaveUsageHint", { used: integer(leave.usedDays, locale), total: integer(leave.totalEntitledDays, locale), year }),
      icon: Calendar,
      color: "cyan",
    });
  }
  if (overtime && overtime.totals.length > 0) {
    cards.push({
      key: "overtime",
      label: t("reports:kpis.cards.overtimeCost"),
      value: overtime.totals.map((total) => formatMoney(total.amount, total.currency, i18n.language)).join(" · "),
      hint: t("reports:kpis.cards.overtimeCostHint", { month: monthLabel, employeeCount: overtime.employeeCount }),
      icon: Clock,
      color: "violet",
    });
  }

  if (cards.length === 0) {
    return (
      <Card withBorder padding="lg" radius="md">
        <Text size="sm" c="dimmed">
          {t("reports:kpis.empty")}
        </Text>
      </Card>
    );
  }

  return (
    <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
      {cards.map((card) => {
        const Icon = card.icon;
        return (
          <Card key={card.key} withBorder padding="lg" radius="md">
            <Group justify="space-between" align="flex-start" wrap="nowrap">
              <div>
                <Text size="xs" fw={600} c="dimmed" tt="uppercase" lts={0.5}>
                  {card.label}
                </Text>
                <Text size="1.75rem" fw={700} mt={6} lh={1.1}>
                  {card.value}
                </Text>
              </div>
              <ThemeIcon variant="light" color={card.color} size={40} radius="md">
                <Icon size={20} />
              </ThemeIcon>
            </Group>
            {card.hint && (
              <Text size="xs" c="dimmed" mt="sm">
                {card.hint}
              </Text>
            )}
          </Card>
        );
      })}
    </SimpleGrid>
  );
}

function GenderDonut({ breakdown, locale, dimensionLabel }: { breakdown: KpiBreakdown[]; locale: string; dimensionLabel: (d: string) => string }) {
  const { t } = useTranslation(["reports"]);
  const ordered = [...breakdown].sort((a, b) => GENDER_ORDER.indexOf(a.dimension) - GENDER_ORDER.indexOf(b.dimension));
  // G-10-21: a suppressed cell's count never reached the client - it draws no slice, only a legend line.
  const data = ordered
    .filter((b) => !b.isSuppressed)
    .map((b) => ({ name: dimensionLabel(b.dimension), value: b.count ?? 0, color: GENDER_COLOR[b.dimension] ?? "gray.5" }));
  return (
    <Group align="center" gap="lg" wrap="wrap" justify="center">
      <DonutChart size={170} thickness={24} data={data} withTooltip paddingAngle={2} />
      <Stack gap={6} component="ul" className="m-0 list-none p-0">
        {ordered.map((b) => {
          const color = GENDER_COLOR[b.dimension] ?? "gray.5";
          return (
            <Group key={b.dimension} component="li" gap="xs" wrap="nowrap">
              <span aria-hidden className="inline-block h-3 w-3 rounded-sm" style={{ background: `var(--mantine-color-${color.replace(".", "-")})` }} />
              <Text size="sm">{dimensionLabel(b.dimension)}</Text>
              <Text size="sm" c="dimmed">
                {b.isSuppressed ? t("reports:kpis.charts.suppressed") : `${integer(b.count ?? 0, locale)} · ${percent(b.share ?? 0, locale)}`}
              </Text>
            </Group>
          );
        })}
      </Stack>
    </Group>
  );
}

function ChartCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Card withBorder padding="lg" radius="md">
      <Text fw={600} size="sm" mb="sm">
        {title}
      </Text>
      {children}
    </Card>
  );
}

function EmptyChart() {
  const { t } = useTranslation(["reports"]);
  return (
    <Text size="sm" c="dimmed" ta="center" py="xl">
      {t("reports:kpis.charts.noData")}
    </Text>
  );
}
