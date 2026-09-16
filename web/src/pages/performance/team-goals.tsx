import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { ActionIcon, Badge, Card, Group, Progress, Text, Title, Tooltip } from "@mantine/core";
import { Users, X } from "lucide-react";
import ConfirmDialog from "@/components/confirm-dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { cancelGoal, getTeamGoals, type Goal, type GoalStatus } from "@/services/performance.service";

const TEAM_GOALS_KEY = ["performance", "goals", "team"] as const;

const STATUS_BADGE: Record<GoalStatus, { color?: string; variant: "light" | "filled" | "outline" }> = {
  open: { variant: "light" },
  inProgress: { variant: "light", color: "blue" },
  completed: { variant: "filled", color: "green" },
  cancelled: { variant: "outline", color: "gray" },
  archived: { variant: "outline", color: "gray" },
};

export default function TeamGoalsPage() {
  const canView = usePermission("performance.team.goals");
  if (!canView) return <NoAccess />;
  return <TeamGoals />;
}

function TeamGoals() {
  const { t, i18n } = useTranslation(["performance", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("performance.goals.manage");

  const goalsQuery = useQuery({ queryKey: TEAM_GOALS_KEY, queryFn: getTeamGoals });
  const goals = useMemo(() => goalsQuery.data ?? [], [goalsQuery.data]);

  const [cancelTarget, setCancelTarget] = useState<Goal | null>(null);
  const cancelMutation = useMutation({
    mutationFn: (id: string) => cancelGoal(id),
    onSuccess: () => {
      setCancelTarget(null);
      void queryClient.invalidateQueries({ queryKey: TEAM_GOALS_KEY });
      toast({ variant: "success", title: t("common:success"), description: t("performance:team.cancelled") });
    },
    onError: (err) => {
      setCancelTarget(null);
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(err, t("performance:genericError")) });
    },
  });

  const columns: ColumnDef<Goal>[] = [
    {
      id: "employee",
      header: t("performance:team.col.employee"),
      meta: { label: t("performance:team.col.employee") },
      cell: ({ row }) => row.original.employeeName ?? `#${row.original.employeeId.slice(0, 8)}`,
    },
    {
      id: "title",
      accessorKey: "title",
      header: t("performance:team.col.title"),
      meta: { label: t("performance:team.col.title") },
      cell: ({ row }) => <Text fw={500}>{row.original.title}</Text>,
    },
    {
      id: "targetDate",
      accessorKey: "targetDate",
      header: t("performance:team.col.targetDate"),
      meta: { label: t("performance:team.col.targetDate") },
      cell: ({ row }) => formatDateOnly(row.original.targetDate, i18n.language),
    },
    {
      id: "progress",
      header: t("performance:team.col.progress"),
      meta: { label: t("performance:team.col.progress") },
      enableSorting: false,
      cell: ({ row }) => (
        <div className="min-w-[8rem]">
          <Progress value={row.original.progress} size="sm" aria-label={`${row.original.progress}%`} />
        </div>
      ),
    },
    {
      id: "status",
      accessorKey: "status",
      header: t("performance:team.col.status"),
      meta: { label: t("performance:team.col.status") },
      cell: ({ row }) => <Badge {...STATUS_BADGE[row.original.status]}>{t(`performance:status.${row.original.status}`)}</Badge>,
    },
    {
      id: "actions",
      header: "",
      meta: { label: t("common:actions") },
      enableSorting: false,
      cell: ({ row }) =>
        canManage &&
        (row.original.status === "open" || row.original.status === "inProgress") && (
          <Group justify="flex-end">
            <Tooltip label={t("performance:team.cancel")}>
              <ActionIcon type="button" variant="subtle" color="red" aria-label={t("performance:team.cancel")} onClick={() => setCancelTarget(row.original)}>
                <X size={16} />
              </ActionIcon>
            </Tooltip>
          </Group>
        ),
    },
  ];

  const table = useDataTable({ columns, data: goals, getRowId: (row) => row.id });

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-4 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <Users className="h-6 w-6" /> {t("performance:team.title")}
        </Title>
        <Text c="dimmed">{t("performance:team.subtitle")}</Text>
      </div>

      <Card withBorder padding="lg">
        {goalsQuery.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {getApiErrorMessage(goalsQuery.error, t("performance:genericError"))}
          </Text>
        ) : (
          <DataTable table={table} isLoading={goalsQuery.isLoading} emptyMessage={t("performance:team.empty")} />
        )}
      </Card>

      <ConfirmDialog
        open={Boolean(cancelTarget)}
        onOpenChange={(open) => !open && setCancelTarget(null)}
        title={t("performance:team.cancelConfirmTitle")}
        description={t("performance:team.cancelConfirmDescription", { title: cancelTarget?.title ?? "" })}
        confirmLabel={t("performance:team.cancel")}
        isLoading={cancelMutation.isPending}
        onConfirm={() => cancelTarget && cancelMutation.mutate(cancelTarget.id)}
      />
    </div>
  );
}
