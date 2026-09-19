import { useNavigate } from "react-router";
import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Badge, Button, Group, Skeleton, Text, Title } from "@mantine/core";
import { ClipboardCheck, Plus } from "lucide-react";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { listReviewCycles, type ReviewCycleStatus, type ReviewCycleSummaryDto } from "@/services/performance.service";

const CYCLES_KEY = ["performance", "cycles"] as const;

const STATUS_COLOR: Record<ReviewCycleStatus, string> = { draft: "gray", published: "blue", completed: "green" };

export default function CyclesListPage() {
  const canView = usePermission("performance.cycle.view");
  if (!canView) return <NoAccess />;
  return <CyclesList />;
}

function CyclesList() {
  const { t, i18n } = useTranslation(["performance", "common"]);
  const navigate = useNavigate();
  const canCreate = usePermission("performance.cycle.create");
  const query = useQuery({ queryKey: CYCLES_KEY, queryFn: listReviewCycles });

  const columns: ColumnDef<ReviewCycleSummaryDto>[] = [
    {
      id: "name",
      accessorFn: (row) => row.name,
      header: t("performance:cycles.col.name"),
      meta: { label: t("performance:cycles.col.name") },
    },
    {
      id: "period",
      accessorFn: (row) => `${row.periodStart}-${row.periodEnd}`,
      header: t("performance:cycles.col.period"),
      meta: { label: t("performance:cycles.col.period") },
      cell: ({ row }) => (
        <Text size="sm">
          {formatDateOnly(row.original.periodStart, i18n.language)} — {formatDateOnly(row.original.periodEnd, i18n.language)}
        </Text>
      ),
    },
    {
      id: "status",
      accessorFn: (row) => row.status,
      header: t("performance:cycles.col.status"),
      meta: { label: t("performance:cycles.col.status") },
      cell: ({ row }) => <Badge color={STATUS_COLOR[row.original.status]}>{t(`performance:cycleStatus.${row.original.status}`)}</Badge>,
    },
    {
      id: "participants",
      accessorFn: (row) => row.participantCount,
      header: t("performance:cycles.col.participants"),
      meta: { label: t("performance:cycles.col.participants") },
      cell: ({ row }) => (
        <Text size="sm">
          {row.original.completedParticipantCount} / {row.original.participantCount}
        </Text>
      ),
    },
  ];

  const table = useDataTable({
    columns,
    data: query.data ?? [],
    getRowId: (row) => row.id,
    columnVisibilityKey: "senseik.performance.cycles.columns",
    initialSorting: [{ id: "period", desc: true }],
  });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <ClipboardCheck size={24} /> {t("performance:cycles.title")}
          </Title>
          <Text c="dimmed">{t("performance:cycles.subtitle")}</Text>
        </div>
        {canCreate && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => navigate("/app/performance/cycles/new")}>
            {t("performance:cycles.new")}
          </Button>
        )}
      </Group>

      {query.isLoading ? (
        <Skeleton h={256} />
      ) : query.isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(query.error, t("performance:genericError"))}
        </Text>
      ) : (
        <div className="space-y-2">
          <Group justify="flex-end">
            <DataTableViewOptions table={table} />
          </Group>
          <DataTable
            table={table}
            emptyMessage={t("performance:cycles.empty")}
            onRowClick={(row) => navigate(`/app/performance/cycles/${row.id}`)}
          />
        </div>
      )}
    </div>
  );
}
