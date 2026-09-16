import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { BarChart3, X } from "lucide-react";
import { Button, Card, Group, Input, SimpleGrid, Skeleton, Text, Title } from "@mantine/core";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { DatePicker } from "@/components/ui/date-picker";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { getWorkforceKpis, type KpiResult } from "@/services/analytics.service";

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

export default function ReportsPage() {
  const canView = usePermission("analytics.kpi.view");
  if (!canView) return <NoAccess />;
  return <WorkforceReports />;
}

function WorkforceReports() {
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
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <BarChart3 size={24} /> {t("reports:title")}
          </Title>
          <Text c="dimmed">{t("reports:subtitle")}</Text>
        </div>
      </Group>

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
      </SimpleGrid>

      <Card withBorder padding={0}>
        <DataTable table={table} isLoading={isLoading} isFetching={isFetching} emptyMessage={t("reports:breakdown.empty")} />
      </Card>
    </div>
  );
}
