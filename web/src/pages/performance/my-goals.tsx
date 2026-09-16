import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useForm, Controller, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge, Button, Card, Group, Modal, Progress, Select, Skeleton, Slider, Stack, Text, Textarea, TextInput, Title } from "@mantine/core";
import { Plus, Target } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  createMyGoal,
  getMyGoals,
  getMyReviewCycles,
  updateMyGoalProgress,
  type CreateMyGoalInput,
  type Goal,
  type GoalStatus,
} from "@/services/performance.service";

const MY_GOALS_KEY = ["performance", "goals", "me"] as const;

const STATUS_BADGE: Record<GoalStatus, { color?: string; variant: "light" | "filled" | "outline" }> = {
  open: { variant: "light" },
  inProgress: { variant: "light", color: "blue" },
  completed: { variant: "filled", color: "green" },
  cancelled: { variant: "outline", color: "gray" },
  archived: { variant: "outline", color: "gray" },
};

function GoalStatusBadge({ status }: { status: GoalStatus }) {
  const { t } = useTranslation(["performance"]);
  return <Badge {...STATUS_BADGE[status]}>{t(`performance:status.${status}`)}</Badge>;
}

function goalErrorMessage(error: unknown, t: (key: string) => string): string {
  const problem = getApiProblem(error);
  if (problem?.code === "performance.goal.target_date_outside_cycle") return t("performance:goals.errors.targetDateOutsideCycle");
  if (problem?.code === "performance.review.immutable") return t("performance:goals.errors.reviewImmutable");
  return getApiErrorMessage(error, t("performance:genericError"));
}

const schema = z.object({
  title: z.string().min(1),
  description: z.string().optional(),
  targetDate: z.string().min(1),
  reviewCycleId: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

export default function MyGoalsPage() {
  const canGoals = usePermission("performance.my.goals");
  if (!canGoals) return <NoAccess />;
  return <MyGoals />;
}

function MyGoals() {
  const { t, i18n } = useTranslation(["performance", "common"]);
  const queryClient = useQueryClient();
  const canReviews = usePermission("performance.my.reviews");

  const goalsQuery = useQuery({ queryKey: MY_GOALS_KEY, queryFn: getMyGoals });
  const cyclesQuery = useQuery({ queryKey: ["performance", "review-cycles", "me"], queryFn: getMyReviewCycles, enabled: canReviews });

  const [addOpen, setAddOpen] = useState(false);
  const [progressTarget, setProgressTarget] = useState<Goal | null>(null);

  const invalidate = () => queryClient.invalidateQueries({ queryKey: MY_GOALS_KEY });

  const {
    register,
    handleSubmit,
    reset,
    control,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { title: "", description: "", targetDate: "", reviewCycleId: "" },
  });

  const selectedCycleId = useWatch({ control, name: "reviewCycleId" });
  const selectedCycle = (cyclesQuery.data ?? []).find((cycle) => cycle.id === selectedCycleId);

  const createMutation = useMutation({
    mutationFn: (input: CreateMyGoalInput) => createMyGoal(input),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("performance:my.created") });
      setAddOpen(false);
      reset();
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: goalErrorMessage(err, t) }),
  });

  function submit(values: FormValues) {
    createMutation.mutate({
      title: values.title,
      description: values.description?.trim() || null,
      targetDate: values.targetDate,
      reviewCycleId: values.reviewCycleId || null,
    });
  }

  const date = (value: string) => formatDateOnly(value, i18n.language);

  return (
    <div className="mx-auto max-w-4xl space-y-6 p-4 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm" wrap="wrap">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Target className="h-6 w-6" /> {t("performance:my.title")}
          </Title>
          <Text c="dimmed">{t("performance:my.subtitle")}</Text>
        </div>
        <Button type="button" leftSection={<Plus size={16} />} onClick={() => setAddOpen(true)}>
          {t("performance:my.addGoal")}
        </Button>
      </Group>

      {goalsQuery.isLoading ? (
        <Stack gap="md">
          <Skeleton h={120} />
          <Skeleton h={120} />
        </Stack>
      ) : goalsQuery.isError ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(goalsQuery.error, t("performance:genericError"))}
        </Text>
      ) : !goalsQuery.data || goalsQuery.data.length === 0 ? (
        <Card withBorder padding="lg">
          <Stack gap="sm" align="center">
            <Text size="sm" c="dimmed" ta="center">
              {t("performance:my.empty")}
            </Text>
            <Button type="button" leftSection={<Plus size={16} />} onClick={() => setAddOpen(true)}>
              {t("performance:my.emptyCta")}
            </Button>
          </Stack>
        </Card>
      ) : (
        <Stack gap="md">
          {goalsQuery.data.map((goal) => (
            <Card key={goal.id} withBorder padding="lg">
              <Stack gap="xs">
                <Group justify="space-between" align="flex-start" wrap="nowrap">
                  <Text fw={600}>{goal.title}</Text>
                  <GoalStatusBadge status={goal.status} />
                </Group>
                {goal.description && (
                  <Text size="sm" c="dimmed">
                    {goal.description}
                  </Text>
                )}
                <Text size="xs" c="dimmed">
                  {t("performance:my.targetDate", { date: date(goal.targetDate) })}
                </Text>
                <div>
                  <Progress value={goal.progress} size="sm" aria-label={`${goal.progress}%`} />
                  <Text size="xs" c="dimmed" mt={4}>
                    {t("performance:my.progressLabel", { progress: goal.progress })}
                  </Text>
                </div>
                {(goal.status === "open" || goal.status === "inProgress") && (
                  <Group justify="flex-end">
                    <Button type="button" size="xs" variant="default" onClick={() => setProgressTarget(goal)}>
                      {t("performance:my.updateProgress")}
                    </Button>
                  </Group>
                )}
              </Stack>
            </Card>
          ))}
        </Stack>
      )}

      <Modal opened={addOpen} onClose={() => setAddOpen(false)} title={t("performance:form.newTitle")}>
        <form onSubmit={handleSubmit(submit)}>
          <Stack gap="md">
            <TextInput
              data-autofocus
              label={t("performance:form.title")}
              withAsterisk
              error={errors.title ? t("common:required") : undefined}
              {...register("title")}
            />
            <Textarea label={t("performance:form.description")} minRows={3} autosize {...register("description")} />
            <Controller
              control={control}
              name="targetDate"
              render={({ field }) => (
                <div>
                  <Text size="sm" fw={500} mb={4}>
                    {t("performance:form.targetDate")} <span aria-hidden>*</span>
                  </Text>
                  <DatePicker
                    value={field.value}
                    onChange={field.onChange}
                    min={selectedCycle?.startDate}
                    max={selectedCycle?.endDate}
                  />
                  {errors.targetDate && (
                    <Text size="xs" c="red" mt={4}>
                      {t("common:required")}
                    </Text>
                  )}
                  {selectedCycle && (
                    <Text size="xs" c="dimmed" mt={4}>
                      {t("performance:form.targetDateCycleHint", {
                        start: date(selectedCycle.startDate),
                        end: date(selectedCycle.endDate),
                      })}
                    </Text>
                  )}
                </div>
              )}
            />
            {canReviews && (
              <Controller
                control={control}
                name="reviewCycleId"
                render={({ field }) => (
                  <Select
                    label={t("performance:form.reviewCycle")}
                    placeholder={t("performance:form.reviewCycleNone")}
                    clearable
                    value={field.value || null}
                    onChange={(value) => field.onChange(value ?? "")}
                    onBlur={field.onBlur}
                    data={(cyclesQuery.data ?? []).map((cycle) => ({ value: cycle.id, label: cycle.name }))}
                  />
                )}
              />
            )}
            <Group justify="flex-end" gap="sm">
              <Button type="button" variant="default" onClick={() => setAddOpen(false)} disabled={createMutation.isPending}>
                {t("common:cancel")}
              </Button>
              <Button type="submit" loading={createMutation.isPending}>
                {t("performance:form.submit")}
              </Button>
            </Group>
          </Stack>
        </form>
      </Modal>

      {progressTarget && (
        <ProgressDialog
          goal={progressTarget}
          onClose={() => setProgressTarget(null)}
          onSaved={() => {
            setProgressTarget(null);
            invalidate();
            toast({ variant: "success", title: t("common:success"), description: t("performance:my.progressUpdated") });
          }}
        />
      )}
    </div>
  );
}

function ProgressDialog({ goal, onClose, onSaved }: { goal: Goal; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation(["performance", "common"]);
  const [value, setValue] = useState(goal.progress);
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => updateMyGoalProgress(goal.id, value),
    onSuccess: onSaved,
    onError: (err) => setError(goalErrorMessage(err, t)),
  });

  return (
    <Modal opened onClose={onClose} title={t("performance:my.updateProgress")}>
      <Stack gap="lg">
        <Text size="sm" fw={500}>
          {goal.title}
        </Text>
        <Slider
          value={value}
          onChange={setValue}
          min={0}
          max={100}
          step={5}
          size="lg"
          thumbSize={26}
          marks={[
            { value: 0, label: "0%" },
            { value: 50, label: "50%" },
            { value: 100, label: "100%" },
          ]}
          label={(v) => `${v}%`}
        />
        {error && (
          <Text size="sm" c="red" role="alert">
            {error}
          </Text>
        )}
        <Group justify="flex-end" gap="sm">
          <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button type="button" loading={mutation.isPending} onClick={() => mutation.mutate()}>
            {t("common:save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
