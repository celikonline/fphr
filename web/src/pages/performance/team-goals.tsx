import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useForm, Controller, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { ActionIcon, Badge, Button, Card, Group, Modal, Progress, Select, Stack, Text, Textarea, TextInput, Title, Tooltip } from "@mantine/core";
import { Plus, Users, X } from "lucide-react";
import ConfirmDialog from "@/components/confirm-dialog";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { DatePicker } from "@/components/ui/date-picker";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  cancelGoal,
  createGoal,
  getTeamGoals,
  listReviewCycles,
  type CreateGoalInput,
  type Goal,
  type GoalStatus,
} from "@/services/performance.service";

const TEAM_GOALS_KEY = ["performance", "goals", "team"] as const;

function goalErrorMessage(error: unknown, t: (key: string) => string): string {
  const problem = getApiProblem(error);
  if (problem?.code === "performance.goal.target_date_outside_cycle") return t("performance:goals.errors.targetDateOutsideCycle");
  if (problem?.code === "performance.review.immutable") return t("performance:goals.errors.reviewImmutable");
  return getApiErrorMessage(error, t("performance:genericError"));
}

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

  const [assignOpen, setAssignOpen] = useState(false);
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
      toast({ variant: "destructive", title: t("common:error"), description: goalErrorMessage(err, t) });
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
    <div className="max-w-6xl space-y-6 p-4 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm" wrap="wrap">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Users className="h-6 w-6" /> {t("performance:team.title")}
          </Title>
          <Text c="dimmed">{t("performance:team.subtitle")}</Text>
        </div>
        {canManage && (
          <Button type="button" leftSection={<Plus size={16} />} onClick={() => setAssignOpen(true)}>
            {t("performance:team.assign.action")}
          </Button>
        )}
      </Group>

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

      {canManage && (
        <AssignGoalModal
          open={assignOpen}
          onClose={() => setAssignOpen(false)}
          onAssigned={() => {
            setAssignOpen(false);
            void queryClient.invalidateQueries({ queryKey: TEAM_GOALS_KEY });
            toast({ variant: "success", title: t("common:success"), description: t("performance:team.assign.assigned") });
          }}
        />
      )}
    </div>
  );
}

const assignSchema = z.object({
  employeeId: z.string().min(1),
  title: z.string().min(1).max(200),
  description: z.string().optional(),
  targetDate: z.string().min(1),
  reviewCycleId: z.string().optional(),
});

type AssignFormValues = z.infer<typeof assignSchema>;

function AssignGoalModal({ open, onClose, onAssigned }: { open: boolean; onClose: () => void; onAssigned: () => void }) {
  const { t, i18n } = useTranslation(["performance", "common"]);
  const canReviews = usePermission("performance.reviews.view");
  const cyclesQuery = useQuery({ queryKey: ["performance", "review-cycles", "all"], queryFn: () => listReviewCycles(), enabled: canReviews && open });
  const openCycles = (cyclesQuery.data ?? []).filter((cycle) => cycle.status !== "closed");

  const [pickedEmployee, setPickedEmployee] = useState<PickedEmployee | null>(null);

  const {
    register,
    handleSubmit,
    reset,
    control,
    setValue,
    formState: { errors },
  } = useForm<AssignFormValues>({
    resolver: zodResolver(assignSchema),
    defaultValues: { employeeId: "", title: "", description: "", targetDate: "", reviewCycleId: "" },
  });

  const selectedCycleId = useWatch({ control, name: "reviewCycleId" });
  const selectedCycle = openCycles.find((cycle) => cycle.id === selectedCycleId);
  const date = (value: string) => formatDateOnly(value, i18n.language);

  const mutation = useMutation({
    mutationFn: (input: CreateGoalInput) => createGoal(input),
    onSuccess: () => {
      onAssigned();
      reset();
      setPickedEmployee(null);
    },
    onError: (err) => toast({ variant: "destructive", title: t("common:error"), description: goalErrorMessage(err, t) }),
  });

  function close() {
    reset();
    setPickedEmployee(null);
    onClose();
  }

  function submit(values: AssignFormValues) {
    mutation.mutate({
      employeeId: values.employeeId,
      title: values.title,
      description: values.description?.trim() || null,
      targetDate: values.targetDate,
      reviewCycleId: values.reviewCycleId || null,
    });
  }

  return (
    <Modal opened={open} onClose={close} title={t("performance:team.assign.title")}>
      <form onSubmit={handleSubmit(submit)}>
        <Stack gap="md">
          <Controller
            control={control}
            name="employeeId"
            render={() => (
              <EmployeePicker
                value={pickedEmployee}
                onChange={(picked) => {
                  setPickedEmployee(picked);
                  setValue("employeeId", picked?.id ?? "", { shouldValidate: true });
                }}
                allowClear
                label={t("performance:team.assign.employee")}
                withAsterisk
                error={errors.employeeId ? t("common:required") : undefined}
              />
            )}
          />
          <TextInput
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
                <DatePicker value={field.value} onChange={field.onChange} min={selectedCycle?.startDate} max={selectedCycle?.endDate} />
                {errors.targetDate && (
                  <Text size="xs" c="red" mt={4}>
                    {t("common:required")}
                  </Text>
                )}
                {selectedCycle && (
                  <Text size="xs" c="dimmed" mt={4}>
                    {t("performance:form.targetDateCycleHint", { start: date(selectedCycle.startDate), end: date(selectedCycle.endDate) })}
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
                  data={openCycles.map((cycle) => ({ value: cycle.id, label: cycle.name }))}
                />
              )}
            />
          )}
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={close} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={mutation.isPending}>
              {t("performance:team.assign.submit")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
