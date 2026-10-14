import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Button,
  Card,
  Group,
  Input,
  NativeSelect,
  Progress,
  Skeleton,
  Stack,
  Text,
  Title,
} from "@mantine/core";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import { usePermission } from "@/components/permission-guard";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDate, toDateOnly } from "@/lib/dates";
import { getEmployee } from "@/services/employees.service";
import { enrollEmployee, enumKey, listEmployeeEnrollments, listTrainingPrograms, type EnrollmentStatus } from "@/services/training.service";
import {
  getOnboardingBuddy,
  getEmployeeCategoryProgress,
  listOnboardingTemplateSets,
  saveOnboardingBuddy,
  startOnboardingProcess,
} from "@/services/onboarding.service";

const FULL_PERCENT = 100;

/** Onboarding/offboarding on the employee: per-category progress and starting a named template set (E5). */
export function EmployeeOnboardingCard({ employeeId }: { employeeId: string }) {
  const { t, i18n } = useTranslation(["employees", "onboarding", "common", "training"]);
  const canView = usePermission("onboarding.tasks.view");
  const canManage = usePermission("onboarding.tasks.manage");
  const queryClient = useQueryClient();
  const [setId, setSetId] = useState("");
  const [buddy, setBuddy] = useState<PickedEmployee | null>(null);
  const [buddyStartDate, setBuddyStartDate] = useState<string | null>(null);
  const [buddyEndDate, setBuddyEndDate] = useState<string | null>(null);
  const defaultBuddyEndDate = (() => {
    const date = new Date();
    date.setDate(date.getDate() + 90);
    return toDateOnly(date);
  })();
  const [trainingProgramId, setTrainingProgramId] = useState("");
  const [referenceDate, setReferenceDate] = useState(toDateOnly(new Date()));
  const canManageTrainingEnrollments = usePermission("training.enrollments.manage");
  const canViewTrainingPrograms = usePermission("training.programs.view");
  const canManageTraining = canManageTrainingEnrollments && canViewTrainingPrograms;
  const canViewTrainingEnrollments = usePermission("training.enrollments.view");

  const progress = useQuery({
    queryKey: ["onboarding", "category-progress", employeeId],
    queryFn: () => getEmployeeCategoryProgress(employeeId),
    enabled: canView,
  });
  const sets = useQuery({
    queryKey: ["onboarding", "template-sets"],
    queryFn: listOnboardingTemplateSets,
    enabled: canView && canManage,
  });
  const buddyQuery = useQuery({
    queryKey: ["onboarding", "buddy", employeeId],
    queryFn: () => getOnboardingBuddy(employeeId),
    enabled: canView,
  });
  const savedBuddyEmployee = useQuery({
    queryKey: ["employees", "detail", buddyQuery.data?.buddyEmployeeId],
    queryFn: () => getEmployee(buddyQuery.data!.buddyEmployeeId),
    enabled: Boolean(buddyQuery.data?.buddyEmployeeId),
  });
  const currentBuddy = buddy ?? (savedBuddyEmployee.data
    ? { id: savedBuddyEmployee.data.id, displayName: savedBuddyEmployee.data.displayName }
    : null);
  const trainingPrograms = useQuery({
    queryKey: ["training", "programs"],
    queryFn: listTrainingPrograms,
    enabled: canView && canManageTraining,
  });
  const trainingEnrollments = useQuery({
    queryKey: ["training", "enrollments", "employee", employeeId],
    queryFn: () => listEmployeeEnrollments(employeeId),
    enabled: canView && canViewTrainingEnrollments,
  });
  const saveBuddy = useMutation({
    mutationFn: () => saveOnboardingBuddy(employeeId, {
      buddyEmployeeId: currentBuddy!.id,
      startDate: buddyStartDate ?? buddyQuery.data?.startDate ?? toDateOnly(new Date()),
      endDate: (buddyEndDate ?? buddyQuery.data?.endDate ?? defaultBuddyEndDate) || null,
    }),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("employees:onboardingCard.buddySaved") });
      void queryClient.invalidateQueries({ queryKey: ["onboarding", "buddy", employeeId] });
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("onboarding:actionFailed")) }),
  });
  const enroll = useMutation({
    mutationFn: () => enrollEmployee(trainingProgramId, employeeId),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("employees:onboardingCard.trainingSaved") });
      setTrainingProgramId("");
      void queryClient.invalidateQueries({ queryKey: ["training", "enrollments", "employee", employeeId] });
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("onboarding:actionFailed")) }),
  });
  const start = useMutation({
    mutationFn: () => startOnboardingProcess(employeeId, setId, referenceDate),
    onSuccess: () => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("employees:onboardingCard.started"),
      });
      setSetId("");
      void queryClient.invalidateQueries({ queryKey: ["onboarding"] });
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("onboarding:actionFailed")),
      }),
  });

  if (!canView) return null;

  const activeSets = (sets.data ?? []).filter((set) => set.isActive);
  const rows = progress.data ?? [];

  return (
    <Card withBorder padding="lg">
      <Title order={4} mb="md">
        {t("employees:onboardingCard.title")}
      </Title>
      <Stack gap="md">
        {canManage && (
          <Card withBorder padding="md">
            <Stack gap="sm">
              <Title order={5}>{t("employees:onboardingCard.buddyTitle")}</Title>
              <EmployeePicker
                value={currentBuddy}
                onChange={setBuddy}
                excludeId={employeeId}
                label={t("employees:onboardingCard.buddy")}
                placeholder={t("employees:onboardingCard.buddyPlaceholder")}
              />
              <Group align="flex-end" gap="sm" wrap="wrap">
                <Input.Wrapper label={t("employees:onboardingCard.buddyStart")}>
                  <Input type="date" value={buddyStartDate ?? buddyQuery.data?.startDate ?? toDateOnly(new Date())} onChange={(event) => setBuddyStartDate(event.currentTarget.value)} />
                </Input.Wrapper>
                <Input.Wrapper label={t("employees:onboardingCard.buddyEnd")}>
                  <Input type="date" value={buddyEndDate ?? buddyQuery.data?.endDate ?? defaultBuddyEndDate} onChange={(event) => setBuddyEndDate(event.currentTarget.value)} />
                </Input.Wrapper>
                <Button onClick={() => saveBuddy.mutate()} disabled={!currentBuddy || !(buddyStartDate ?? buddyQuery.data?.startDate)} loading={saveBuddy.isPending}>
                  {t("employees:onboardingCard.saveBuddy")}
                </Button>
              </Group>
            </Stack>
          </Card>
        )}
        {progress.isLoading ? (
          <Skeleton h={64} />
        ) : rows.length === 0 ? (
          <Text size="sm" c="dimmed">
            {t("employees:onboardingCard.none")}
          </Text>
        ) : (
          <Stack gap="xs" aria-label={t("employees:onboardingCard.progress")}>
            {rows.map((row) => (
              <div key={row.categoryId ?? "uncategorized"}>
                <Group justify="space-between">
                  <Text size="sm">{row.name}</Text>
                  <Text size="sm" c="dimmed">{`${row.done}/${row.total}`}</Text>
                </Group>
                <Progress
                  value={row.percent}
                  color={row.percent >= FULL_PERCENT ? "green" : "blue"}
                />
              </div>
            ))}
          </Stack>
        )}
        {canManage && (
          <Group align="flex-end" gap="sm" wrap="wrap">
            <NativeSelect
              label={t("employees:onboardingCard.set")}
              value={setId}
              onChange={(event) => setSetId(event.currentTarget.value)}
              data={[
                { value: "", label: t("employees:onboardingCard.setPlaceholder") },
                ...activeSets.map((set) => ({ value: set.id, label: set.name })),
              ]}
            />
            <Input.Wrapper label={t("employees:onboardingCard.referenceDate")}>
              <DatePicker
                id={`onboarding-card-date-${employeeId}`}
                value={referenceDate}
                onChange={setReferenceDate}
              />
            </Input.Wrapper>
            <Button
              onClick={() => start.mutate()}
              disabled={!setId || !referenceDate}
              loading={start.isPending}
            >
              {t("employees:onboardingCard.start")}
            </Button>
          </Group>
        )}
        {canManageTraining && (
          <Card withBorder padding="md">
            <Stack gap="sm">
              <Title order={5}>{t("employees:onboardingCard.trainingPlan")}</Title>
              {canViewTrainingEnrollments && (trainingEnrollments.data ?? []).length > 0 && (
                <Stack gap="xs">
                  {(trainingEnrollments.data ?? []).map((item) => {
                    const program = (trainingPrograms.data ?? []).find((candidate) => candidate.id === item.trainingProgramId);
                    return (
                      <Group key={item.id} justify="space-between" gap="sm">
                        <Text size="sm">{program?.title ?? item.trainingProgramId}</Text>
                        <Text size="xs" c="dimmed">
                          {[t(`training:enrollmentStatuses.${enumKey<EnrollmentStatus>(item.status)}`), program?.startDate && formatDate(program.startDate, i18n.language)]
                            .filter(Boolean).join(" · ")}
                        </Text>
                      </Group>
                    );
                  })}
                </Stack>
              )}
              <Group align="flex-end" gap="sm" wrap="wrap">
                <NativeSelect
                  label={t("employees:onboardingCard.training")}
                  value={trainingProgramId}
                  onChange={(event) => setTrainingProgramId(event.currentTarget.value)}
                  data={[
                    { value: "", label: t("employees:onboardingCard.trainingPlaceholder") },
                    ...(trainingPrograms.data ?? []).filter((program) => program.status.toLowerCase() === "active")
                      .map((program) => ({ value: program.id, label: `${program.title} · ${formatDate(program.startDate, i18n.language)}` })),
                  ]}
                />
                <Button onClick={() => enroll.mutate()} disabled={!trainingProgramId} loading={enroll.isPending}>
                  {t("employees:onboardingCard.enrollTraining")}
                </Button>
              </Group>
            </Stack>
          </Card>
        )}
      </Stack>
    </Card>
  );
}
