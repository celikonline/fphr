import { useState } from "react";
import { useParams } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Badge, Button, Card, Group, Menu, Modal, Progress, Skeleton, Text, Title } from "@mantine/core";
import { ClipboardCheck, Download, MoreVertical, Target, UserPlus } from "lucide-react";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { useEmployeeNames } from "@/components/performance/employee-names";
import AddParticipantsDialog from "@/components/performance/add-participants-dialog";
import GoalsDialog from "@/components/performance/goals-dialog";
import {
  downloadReviewReport,
  forceCompleteReviewCycle,
  getReviewCycle,
  publishReviewCycle,
  saveDownloadedFile,
  type ReviewCycleStatus,
  type ReviewParticipantDto,
  type ReviewParticipantStatus,
} from "@/services/performance.service";

const STATUS_COLOR: Record<ReviewCycleStatus, string> = { draft: "gray", published: "blue", completed: "green" };
const PARTICIPANT_STATUS_COLOR: Record<ReviewParticipantStatus, string> = { pending: "gray", inProgress: "blue", completed: "green" };

export default function CycleDetailPage() {
  const canView = usePermission("performance.cycle.view");
  if (!canView) return <NoAccess />;
  return <CycleDetail />;
}

function CycleDetail() {
  const { id } = useParams<{ id: string }>();
  const { t, i18n } = useTranslation(["performance", "common"]);
  const queryClient = useQueryClient();
  const canManageParticipants = usePermission("performance.participant.manage");
  const canPublish = usePermission("performance.cycle.publish");
  const canForceComplete = usePermission("performance.cycle.force_complete");
  const canDownload = usePermission("performance.report.download");

  const cycleKey = ["performance", "cycles", id] as const;
  const query = useQuery({ queryKey: cycleKey, queryFn: () => getReviewCycle(id!), enabled: !!id });
  const cycle = query.data;

  const [addingParticipants, setAddingParticipants] = useState(false);
  const [goalsFor, setGoalsFor] = useState<ReviewParticipantDto | null>(null);

  const allEmployeeIds = (cycle?.participants ?? []).map((p) => p.employeeId);
  const employeeName = useEmployeeNames(allEmployeeIds);

  const refresh = () => queryClient.invalidateQueries({ queryKey: cycleKey });

  const publishMutation = useMutation({
    mutationFn: () => publishReviewCycle(id!),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("performance:detail.published") });
      void refresh();
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("performance:genericError")) }),
  });

  const forceCompleteMutation = useMutation({
    mutationFn: () => forceCompleteReviewCycle(id!),
    onSuccess: (count) => {
      toast({ variant: "success", title: t("common:success"), description: t("performance:detail.forceCompleted", { count }) });
      void refresh();
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("performance:genericError")) }),
  });

  async function downloadReport(participant: ReviewParticipantDto, kind: "detailed" | "summary") {
    try {
      const file = await downloadReviewReport(id!, participant.id, participant.employeeId, kind);
      saveDownloadedFile(file);
    } catch (error) {
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("performance:genericError")) });
    }
  }

  const columns: ColumnDef<ReviewParticipantDto>[] = [
    {
      id: "employee",
      accessorFn: (row) => employeeName(row.employeeId),
      header: t("performance:detail.col.employee"),
      meta: { label: t("performance:detail.col.employee") },
    },
    {
      id: "competencyProgress",
      accessorFn: (row) => row.competencyProgress,
      header: t("performance:detail.col.competencyProgress"),
      meta: { label: t("performance:detail.col.competencyProgress") },
      cell: ({ row }) => (
        <Group gap="xs" wrap="nowrap">
          <Progress value={row.original.competencyProgress} w={80} size="sm" />
          <Text size="xs" c="dimmed">
            {row.original.competencyProgress.toFixed(0)}%
          </Text>
        </Group>
      ),
    },
    ...(cycle?.kind === "competencyAndGoal"
      ? [
          {
            id: "goalProgress",
            accessorFn: (row: ReviewParticipantDto) => row.goalProgress,
            header: t("performance:detail.col.goalProgress"),
            meta: { label: t("performance:detail.col.goalProgress") },
            cell: ({ row }: { row: { original: ReviewParticipantDto } }) => (
              <Button type="button" size="xs" variant="subtle" leftSection={<Target size={14} />} onClick={() => setGoalsFor(row.original)}>
                {row.original.goalProgress.toFixed(0)}%
              </Button>
            ),
          } satisfies ColumnDef<ReviewParticipantDto>,
        ]
      : []),
    {
      id: "status",
      accessorFn: (row) => row.status,
      header: t("performance:detail.col.status"),
      meta: { label: t("performance:detail.col.status") },
      cell: ({ row }) => (
        <Badge color={PARTICIPANT_STATUS_COLOR[row.original.status]}>{t(`performance:participantStatus.${row.original.status}`)}</Badge>
      ),
    },
    {
      id: "finalScore",
      accessorFn: (row) => row.finalScore ?? "-",
      header: t("performance:detail.col.finalScore"),
      meta: { label: t("performance:detail.col.finalScore") },
    },
    {
      id: "actions",
      header: "",
      enableSorting: false,
      enableHiding: false,
      cell: ({ row }) =>
        canDownload && row.original.status === "completed" ? (
          <Menu position="bottom-end">
            <Menu.Target>
              <Button type="button" variant="subtle" color="gray" size="xs" px="xs" onClick={(e) => e.stopPropagation()}>
                <MoreVertical size={16} />
              </Button>
            </Menu.Target>
            <Menu.Dropdown>
              <Menu.Item leftSection={<Download size={14} />} onClick={() => downloadReport(row.original, "detailed")}>
                {t("performance:detail.detailedReport")}
              </Menu.Item>
              <Menu.Item leftSection={<Download size={14} />} onClick={() => downloadReport(row.original, "summary")}>
                {t("performance:detail.summaryReport")}
              </Menu.Item>
            </Menu.Dropdown>
          </Menu>
        ) : null,
    },
  ];

  const table = useDataTable({
    columns,
    data: cycle?.participants ?? [],
    getRowId: (row) => row.id,
    columnVisibilityKey: "senseik.performance.participants.columns",
  });

  if (query.isLoading) return <Skeleton h={320} m="xl" />;
  if (query.isError || !cycle) {
    return (
      <Text size="sm" c="red" p="xl">
        {getApiErrorMessage(query.error, t("performance:genericError"))}
      </Text>
    );
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <ClipboardCheck size={24} /> {cycle.name}
          </Title>
          <Text c="dimmed">
            {formatDateOnly(cycle.periodStart, i18n.language)} — {formatDateOnly(cycle.periodEnd, i18n.language)}
          </Text>
        </div>
        <Badge size="lg" color={STATUS_COLOR[cycle.status]}>
          {t(`performance:cycleStatus.${cycle.status}`)}
        </Badge>
      </Group>

      <Group>
        {cycle.status === "draft" && canManageParticipants && (
          <Button type="button" variant="default" leftSection={<UserPlus size={16} />} onClick={() => setAddingParticipants(true)}>
            {t("performance:detail.addParticipants")}
          </Button>
        )}
        {cycle.status === "draft" && canPublish && (
          <Button type="button" loading={publishMutation.isPending} disabled={cycle.participants.length === 0} onClick={() => publishMutation.mutate()}>
            {t("performance:detail.publish")}
          </Button>
        )}
        {cycle.status === "published" && canForceComplete && (
          <Button type="button" loading={forceCompleteMutation.isPending} onClick={() => forceCompleteMutation.mutate()}>
            {t("performance:detail.forceComplete")}
          </Button>
        )}
      </Group>

      <Card withBorder padding="lg">
        <Title order={4} mb="md">
          {t("performance:detail.participants")}
        </Title>
        <DataTable table={table} emptyMessage={t("performance:detail.noParticipants")} />
      </Card>

      {addingParticipants && (
        <AddParticipantsDialog
          cycle={cycle}
          onClose={() => setAddingParticipants(false)}
          onAdded={() => {
            setAddingParticipants(false);
            void refresh();
          }}
        />
      )}

      {goalsFor && (
        <GoalsModal cycleId={cycle.id} participant={goalsFor} employeeName={employeeName} onClose={() => setGoalsFor(null)} onChanged={refresh} />
      )}
    </div>
  );
}

function GoalsModal({
  cycleId,
  participant,
  employeeName,
  onClose,
  onChanged,
}: {
  cycleId: string;
  participant: ReviewParticipantDto;
  employeeName: (id: string) => string;
  onClose: () => void;
  onChanged: () => void;
}) {
  const { t } = useTranslation(["performance", "common"]);
  return (
    <Modal opened onClose={onClose} size="40rem" title={`${t("performance:goals.title")} — ${employeeName(participant.employeeId)}`}>
      <GoalsDialog cycleId={cycleId} participant={participant} onChanged={onChanged} />
    </Modal>
  );
}
