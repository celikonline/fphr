import { useState } from "react";
import { useNavigate } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge, Button, Card, Group, NumberInput, Progress, Skeleton, Stack, Tabs, Text, Textarea, Title } from "@mantine/core";
import { ClipboardCheck, Download, ListChecks, Target } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { useAuthStore } from "@/store/auth.store";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { useEmployeeNames } from "@/components/performance/employee-names";
import FillAssignmentDialog from "@/components/performance/fill-assignment-dialog";
import {
  downloadReviewReport,
  enterGoalResult,
  getMyAssignments,
  getMyGoals,
  getMyResults,
  saveDownloadedFile,
  type MyAssignmentDto,
  type MyGoalDto,
} from "@/services/performance.service";

export default function MyPerformancePage() {
  const canSelfService = usePermission("performance.my.assignments");
  const canViewCycles = usePermission("performance.cycle.view");
  if (!canSelfService && !canViewCycles) return <NoAccess />;
  return <MyPerformance canSelfService={canSelfService} canManage={canViewCycles} />;
}

function MyPerformance({ canSelfService, canManage }: { canSelfService: boolean; canManage: boolean }) {
  const { t, i18n } = useTranslation(["performance", "common"]);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const myEmployeeId = useAuthStore((s) => s.user?.employeeId) ?? "";
  const [filling, setFilling] = useState<MyAssignmentDto | null>(null);

  const assignmentsQuery = useQuery({ queryKey: ["performance", "my-assignments"], queryFn: getMyAssignments, enabled: canSelfService });
  const goalsQuery = useQuery({ queryKey: ["performance", "my-goals"], queryFn: getMyGoals, enabled: canSelfService });
  const resultsQuery = useQuery({ queryKey: ["performance", "my-results"], queryFn: getMyResults, enabled: canSelfService });

  const evaluatedIds = (assignmentsQuery.data ?? []).map((a) => a.participantEmployeeId);
  const employeeName = useEmployeeNames([...evaluatedIds]);

  async function downloadOwnReport(cycleId: string, participantId: string, employeeId: string) {
    try {
      const file = await downloadReviewReport(cycleId, participantId, employeeId, "detailed");
      saveDownloadedFile(file);
    } catch (error) {
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("performance:genericError")) });
    }
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <ClipboardCheck className="h-6 w-6" /> {t("performance:my.title")}
          </Title>
          <Text c="dimmed">{t("performance:my.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" variant="default" onClick={() => navigate("/app/performance/cycles")}>
            {t("performance:my.manage")}
          </Button>
        )}
      </Group>

      {canSelfService && (
        <Tabs defaultValue="assignments">
          <Tabs.List>
            <Tabs.Tab value="assignments" leftSection={<ListChecks size={16} />}>
              {t("performance:my.assignmentsTab")}
            </Tabs.Tab>
            <Tabs.Tab value="goals" leftSection={<Target size={16} />}>
              {t("performance:my.goalsTab")}
            </Tabs.Tab>
            <Tabs.Tab value="results" leftSection={<ClipboardCheck size={16} />}>
              {t("performance:my.resultsTab")}
            </Tabs.Tab>
          </Tabs.List>

          <Tabs.Panel value="assignments" pt="md">
            {assignmentsQuery.isLoading ? (
              <Skeleton h={160} />
            ) : (assignmentsQuery.data ?? []).length === 0 ? (
              <Text size="sm" c="dimmed">
                {t("performance:my.assignmentsEmpty")}
              </Text>
            ) : (
              <Stack gap="sm">
                {(assignmentsQuery.data ?? []).map((assignment) => (
                  <Card key={assignment.assignmentId} withBorder padding="md">
                    <Group justify="space-between" wrap="nowrap">
                      <div>
                        <Text fw={500}>{assignment.reviewCycleName}</Text>
                        <Text size="sm" c="dimmed">
                          {t(`performance:relation.${assignment.relation}`)} — {employeeName(assignment.participantEmployeeId)}
                        </Text>
                        <Text size="xs" c="dimmed">
                          {formatDateOnly(assignment.periodStart, i18n.language)} — {formatDateOnly(assignment.periodEnd, i18n.language)}
                        </Text>
                      </div>
                      <Group gap="xs">
                        <Badge color={assignment.status === "completed" ? "green" : assignment.status === "skipped" ? "gray" : "blue"}>
                          {t(`performance:assignmentStatus.${assignment.status}`)}
                        </Badge>
                        {(assignment.status === "pending" || assignment.status === "inProgress") && (
                          <Button type="button" size="xs" onClick={() => setFilling(assignment)}>
                            {t("performance:my.fillForm")}
                          </Button>
                        )}
                      </Group>
                    </Group>
                  </Card>
                ))}
              </Stack>
            )}
          </Tabs.Panel>

          <Tabs.Panel value="goals" pt="md">
            {goalsQuery.isLoading ? (
              <Skeleton h={160} />
            ) : (goalsQuery.data ?? []).length === 0 ? (
              <Text size="sm" c="dimmed">
                {t("performance:my.goalsEmpty")}
              </Text>
            ) : (
              <Stack gap="md">
                {(goalsQuery.data ?? []).map((cycle) => (
                  <Card key={cycle.reviewCycleId} withBorder padding="md">
                    <Text fw={500} mb="sm">
                      {cycle.reviewCycleName}
                    </Text>
                    <Stack gap="sm">
                      {cycle.goals.map((goal) => (
                        <GoalRow key={goal.goalId} cycleId={cycle.reviewCycleId} goal={goal} myEmployeeId={myEmployeeId} />
                      ))}
                    </Stack>
                  </Card>
                ))}
              </Stack>
            )}
          </Tabs.Panel>

          <Tabs.Panel value="results" pt="md">
            {resultsQuery.isLoading ? (
              <Skeleton h={160} />
            ) : (resultsQuery.data ?? []).length === 0 ? (
              <Text size="sm" c="dimmed">
                {t("performance:my.resultsEmpty")}
              </Text>
            ) : (
              <Stack gap="sm">
                {(resultsQuery.data ?? []).map((result) => (
                  <Card key={result.reviewCycleId} withBorder padding="md">
                    <Group justify="space-between" wrap="nowrap">
                      <div>
                        <Text fw={500}>{result.reviewCycleName}</Text>
                        <Text size="xs" c="dimmed">
                          {formatDateOnly(result.periodStart, i18n.language)} — {formatDateOnly(result.periodEnd, i18n.language)}
                        </Text>
                        <Group gap="xs" mt={4}>
                          <Progress value={result.competencyProgress} w={100} size="sm" />
                          <Text size="xs" c="dimmed">
                            {t("performance:my.competencyProgress", { value: result.competencyProgress.toFixed(0) })}
                          </Text>
                        </Group>
                      </div>
                      <Group gap="xs">
                        {result.finalScore != null && (
                          <Badge size="lg" color="green">
                            {result.finalScore}
                          </Badge>
                        )}
                        {result.status === "completed" && result.canDownloadPdf && (
                          <Button
                            type="button"
                            size="xs"
                            variant="default"
                            leftSection={<Download size={14} />}
                            onClick={() => void downloadOwnReport(result.reviewCycleId, result.participantId, myEmployeeId)}
                          >
                            {t("performance:my.downloadReport")}
                          </Button>
                        )}
                      </Group>
                    </Group>
                  </Card>
                ))}
              </Stack>
            )}
          </Tabs.Panel>
        </Tabs>
      )}

      {filling && (
        <FillAssignmentDialog
          cycleId={filling.reviewCycleId}
          assignmentId={filling.assignmentId}
          onClose={() => setFilling(null)}
          onSubmitted={() => {
            setFilling(null);
            void queryClient.invalidateQueries({ queryKey: ["performance", "my-assignments"] });
          }}
        />
      )}
    </div>
  );
}

function GoalRow({ cycleId, goal, myEmployeeId }: { cycleId: string; goal: MyGoalDto; myEmployeeId: string }) {
  const { t } = useTranslation(["performance", "common"]);
  const queryClient = useQueryClient();
  const [entering, setEntering] = useState(false);
  const [actualValue, setActualValue] = useState("");
  const [resultComment, setResultComment] = useState("");

  const mutation = useMutation({
    mutationFn: () => enterGoalResult(cycleId, goal.participantId, goal.goalId, myEmployeeId, Number(actualValue), resultComment.trim() || null),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["performance", "my-goals"] });
      toast({ variant: "success", title: t("common:success"), description: t("performance:goals.resultEntered") });
      setEntering(false);
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("performance:genericError")) }),
  });

  return (
    <div>
      <Group justify="space-between">
        <Text fw={500}>{goal.name}</Text>
        <Badge>{t(`performance:goalStatus.${goal.status}`)}</Badge>
      </Group>
      {goal.description && (
        <Text size="sm" c="dimmed">
          {goal.description}
        </Text>
      )}
      <Text size="sm">
        {t("performance:goals.target")}: {goal.targetValue}
        {goal.actualValue != null && ` · ${t("performance:goals.actual")}: ${goal.actualValue}`}
      </Text>
      {goal.status === "approved" &&
        (entering ? (
          <Stack gap="xs" mt="xs">
            <NumberInput label={t("performance:goals.actual")} value={actualValue} onChange={(v) => setActualValue(String(v ?? ""))} />
            <Textarea label={t("performance:goals.resultComment")} value={resultComment} onChange={(e) => setResultComment(e.currentTarget.value)} />
            <Group gap="xs" justify="flex-end">
              <Button type="button" variant="default" size="xs" onClick={() => setEntering(false)}>
                {t("common:cancel")}
              </Button>
              <Button type="button" size="xs" loading={mutation.isPending} disabled={!actualValue} onClick={() => mutation.mutate()}>
                {t("common:save")}
              </Button>
            </Group>
          </Stack>
        ) : (
          <Button type="button" size="xs" variant="light" mt="xs" onClick={() => setEntering(true)}>
            {t("performance:goals.enterResult")}
          </Button>
        ))}
    </div>
  );
}
