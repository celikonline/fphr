import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Wallet } from "lucide-react";
import { Badge, Card, Text, Title } from "@mantine/core";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { getApiErrorMessage } from "@/lib/api-error";
import { listPayrollPeriods, type PayrollPeriod, type PayrollPeriodStatus } from "@/services/payroll.service";

const PERIODS_QUERY_KEY = ["payroll", "periods"] as const;

const STATUS_COLOR: Record<PayrollPeriodStatus, string> = {
  open: "blue",
  approved: "teal",
  paid: "green",
  locked: "gray",
};

/**
 * Bordro dönemleri - salt okunur liste (Faz 7 tur 3 web dilimi). Dönem açma/çalıştırma ve "Değişenler" filtresi
 * bu dilimin dışında; bu sayfa yalnızca `PayrollPeriodsController`'ın dönem listesini gösterir.
 */
export default function PayrollPeriodsPage() {
  const canView = usePermission("payroll.period.recalculate");
  if (!canView) return <NoAccess />;
  return <PayrollPeriodsList />;
}

function PayrollPeriodsList() {
  const { t, i18n } = useTranslation(["payroll", "common"]);

  const periodsQuery = useQuery({ queryKey: PERIODS_QUERY_KEY, queryFn: listPayrollPeriods });
  const periods = useMemo(() => periodsQuery.data ?? [], [periodsQuery.data]);

  const columns: ColumnDef<PayrollPeriod>[] = [
    {
      id: "period",
      header: t("payroll:periods.columns.period"),
      meta: { label: t("payroll:periods.columns.period") },
      cell: ({ row }) =>
        new Intl.DateTimeFormat(i18n.language, { year: "numeric", month: "long" }).format(
          new Date(row.original.year, row.original.month - 1, 1),
        ),
    },
    {
      id: "status",
      accessorKey: "status",
      header: t("payroll:periods.columns.status"),
      meta: { label: t("payroll:periods.columns.status"), headerClassName: "w-40" },
      cell: ({ row }) => (
        <Badge color={STATUS_COLOR[row.original.status] ?? "gray"} variant="light">
          {t(`payroll:periods.status.${row.original.status}`, { defaultValue: row.original.status })}
        </Badge>
      ),
    },
  ];

  const table = useDataTable({ columns, data: periods, getRowId: (r) => r.id });

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <Wallet size={24} /> {t("payroll:periods.title")}
        </Title>
        <Text c="dimmed">{t("payroll:periods.subtitle")}</Text>
      </div>

      <Card withBorder padding="lg">
        {periodsQuery.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {getApiErrorMessage(periodsQuery.error, t("payroll:genericError"))}
          </Text>
        ) : (
          <DataTable table={table} isLoading={periodsQuery.isLoading} emptyMessage={t("payroll:periods.empty")} />
        )}
      </Card>
    </div>
  );
}
