import { useMemo, useState } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Plus, Wallet } from "lucide-react";
import { Badge, Button, Card, Group, Stack, Text, Title } from "@mantine/core";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { getApiErrorMessage } from "@/lib/api-error";
import { PeriodOpenModal } from "./period-open-modal";
import { PeriodsEmptyState } from "./periods-empty-state";
import { listPayrollPeriods, type PayrollPeriod, type PayrollPeriodStatus } from "@/services/payroll.service";

const PERIODS_QUERY_KEY = ["payroll", "periods"] as const;

const STATUS_COLOR: Record<PayrollPeriodStatus, string> = {
  open: "blue",
  approved: "teal",
  paid: "green",
  locked: "gray",
};

/**
 * Bordro dönemleri listesi (Faz 7 tur 3 web dilimi) + "Dönem aç" eylemi. Hesaplama dönem detayında yapılır;
 * "Değişenler" filtresi bu dilimin dışında.
 */
export default function PayrollPeriodsPage() {
  const canView = usePermission("payroll.period.recalculate");
  if (!canView) return <NoAccess />;
  return <PayrollPeriodsList />;
}

function PayrollPeriodsList() {
  const { t, i18n } = useTranslation(["payroll", "common"]);

  const periodsQuery = useQuery({ queryKey: PERIODS_QUERY_KEY, queryFn: listPayrollPeriods });
  const [openDialog, setOpenDialog] = useState(false);
  const periods = useMemo(() => periodsQuery.data ?? [], [periodsQuery.data]);

  const periodLabel = (period: PayrollPeriod) =>
    new Intl.DateTimeFormat(i18n.language, { year: "numeric", month: "long" }).format(new Date(period.year, period.month - 1, 1));

  const columns: ColumnDef<PayrollPeriod>[] = [
    {
      id: "period",
      header: t("payroll:periods.columns.period"),
      meta: { label: t("payroll:periods.columns.period") },
      cell: ({ row }) => (
        <Link to={`/app/payroll/periods/${row.original.id}`} className="font-medium hover:underline">
          {periodLabel(row.original)}
        </Link>
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

  const emptyState = <PeriodsEmptyState onOpenPeriod={() => setOpenDialog(true)} />;

  return (
    <div className="w-full space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Wallet size={24} /> {t("payroll:periods.title")}
          </Title>
          <Text c="dimmed">{t("payroll:periods.subtitle")}</Text>
        </div>
        <Button leftSection={<Plus size={16} />} onClick={() => setOpenDialog(true)}>
          {t("payroll:periods.openAction")}
        </Button>
      </Group>

      <PeriodOpenModal opened={openDialog} onClose={() => setOpenDialog(false)} />

      <Card withBorder padding="lg">
        {periodsQuery.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {getApiErrorMessage(periodsQuery.error, t("payroll:genericError"))}
          </Text>
        ) : (
          <>
            {/* Geniş ekran: tablo */}
            <div className="hidden md:block">
              <DataTable table={table} isLoading={periodsQuery.isLoading} emptyMessage={emptyState} />
            </div>

            {/* Dar ekran (≤390px dahil): kart listesi */}
            <div className="md:hidden">
              {periodsQuery.isLoading ? (
                <Text size="sm" c="dimmed">
                  {t("common:loading")}
                </Text>
              ) : periods.length === 0 ? (
                emptyState
              ) : (
                <Stack gap="sm">
                  {periods.map((period) => (
                    <Card key={period.id} component={Link} to={`/app/payroll/periods/${period.id}`} withBorder padding="sm" radius="md">
                      <Group justify="space-between">
                        <Text fw={500} size="sm">
                          {periodLabel(period)}
                        </Text>
                        <Badge color={STATUS_COLOR[period.status] ?? "gray"} variant="light">
                          {t(`payroll:periods.status.${period.status}`, { defaultValue: period.status })}
                        </Badge>
                      </Group>
                    </Card>
                  ))}
                </Stack>
              )}
            </div>
          </>
        )}
      </Card>
    </div>
  );
}
