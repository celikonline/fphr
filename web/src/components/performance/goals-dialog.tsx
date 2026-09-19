import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { Badge, Button, Group, Stack, Text } from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { usePermission } from "@/components/permission-guard";
import { approveGoalResults, approveGoals, type GoalStatus, type ReviewParticipantDto } from "@/services/performance.service";

const STATUS_COLOR: Record<GoalStatus, string> = { draft: "gray", approved: "blue", resultEntered: "yellow", resultApproved: "green" };

/** HR/manager taraf: bir katılımcının hedeflerini gözden geçirme + toplu onaylama. Hedef ekleme/sonuç girme self-servis tarafında. */
export default function GoalsDialog({
  cycleId,
  participant,
  onChanged,
}: {
  cycleId: string;
  participant: ReviewParticipantDto;
  onChanged: () => void;
}) {
  const { t } = useTranslation(["performance", "common"]);
  const canApprove = usePermission("performance.goals.approve");
  const canApproveResults = usePermission("performance.goals.approve_result");

  const hasDraft = participant.goals.some((g) => g.status === "draft");
  const hasResultEntered = participant.goals.some((g) => g.status === "resultEntered");

  const approveMutation = useMutation({
    mutationFn: () => approveGoals(cycleId, participant.id, participant.employeeId),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("performance:goals.approved") });
      onChanged();
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("performance:genericError")) }),
  });

  const approveResultsMutation = useMutation({
    mutationFn: () => approveGoalResults(cycleId, participant.id, participant.employeeId),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("performance:goals.resultsApproved") });
      onChanged();
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("performance:genericError")) }),
  });

  return (
    <Stack gap="md">
      {participant.goals.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("performance:goals.empty")}
        </Text>
      ) : (
        <Stack gap="sm">
          {participant.goals.map((goal) => (
            <div key={goal.id}>
              <Group justify="space-between">
                <Text fw={500}>{goal.name}</Text>
                <Badge color={STATUS_COLOR[goal.status]}>{t(`performance:goalStatus.${goal.status}`)}</Badge>
              </Group>
              {goal.description && (
                <Text size="sm" c="dimmed">
                  {goal.description}
                </Text>
              )}
              <Text size="sm">
                {t("performance:goals.target")}: {goal.targetValue}
                {goal.actualValue != null && ` · ${t("performance:goals.actual")}: ${goal.actualValue}`}
                {goal.points != null && ` · ${t("performance:goals.points")}: ${goal.points}`}
              </Text>
              {goal.resultComment && (
                <Text size="sm" c="dimmed">
                  {goal.resultComment}
                </Text>
              )}
            </div>
          ))}
        </Stack>
      )}

      <Group justify="flex-end" gap="xs">
        {canApprove && (
          <Button type="button" variant="default" disabled={!hasDraft} loading={approveMutation.isPending} onClick={() => approveMutation.mutate()}>
            {t("performance:goals.approve")}
          </Button>
        )}
        {canApproveResults && (
          <Button
            type="button"
            disabled={!hasResultEntered}
            loading={approveResultsMutation.isPending}
            onClick={() => approveResultsMutation.mutate()}
          >
            {t("performance:goals.approveResults")}
          </Button>
        )}
      </Group>
    </Stack>
  );
}
