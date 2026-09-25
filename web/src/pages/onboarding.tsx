import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ListChecks } from "lucide-react";
import {
  Button,
  Card,
  Group,
  Input,
  SegmentedControl,
  Skeleton,
  Stack,
  Tabs,
  Text,
  Title,
} from "@mantine/core";
import { EmployeeOnboardingCard } from "@/components/employees/employee-onboarding-card";
import EmployeePicker, { type PickedEmployee } from "@/components/employees/employee-picker";
import NoAccess from "@/components/no-access";
import OnboardingTaskTable from "@/components/onboarding/onboarding-task-table";
import OnboardingTemplates from "@/components/onboarding/onboarding-templates";
import { usePermission } from "@/components/permission-guard";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { toDateOnly } from "@/lib/dates";
import {
  ONBOARDING_STAGES,
  createEmployeeChecklist,
  listEmployeeOnboardingTasks,
  listPendingOnboardingTasks,
  type OnboardingStage,
} from "@/services/onboarding.service";

/** İşe alış / işten çıkış: açık görevler, çalışan görev listesi, şablonlar. */
export default function OnboardingPage() {
  const canView = usePermission("onboarding.tasks.view");
  if (!canView) return <NoAccess />;
  return <OnboardingScreen />;
}

function OnboardingScreen() {
  const { t } = useTranslation(["onboarding"]);
  const canTemplates = usePermission("onboarding.templates.manage");

  return (
    <Stack gap="md" className="max-w-7xl p-6 md:p-10">
      <Title order={2}>{t("onboarding:title")}</Title>
      <Tabs defaultValue="pending" keepMounted={false}>
        <Tabs.List>
          <Tabs.Tab value="pending">{t("onboarding:tabs.pending")}</Tabs.Tab>
          <Tabs.Tab value="employee">{t("onboarding:tabs.employee")}</Tabs.Tab>
          {canTemplates && <Tabs.Tab value="templates">{t("onboarding:tabs.templates")}</Tabs.Tab>}
        </Tabs.List>
        <Tabs.Panel value="pending" pt="md">
          <PendingTasks />
        </Tabs.Panel>
        <Tabs.Panel value="employee" pt="md">
          <EmployeeChecklist />
        </Tabs.Panel>
        {canTemplates && (
          <Tabs.Panel value="templates" pt="md">
            <OnboardingTemplates />
          </Tabs.Panel>
        )}
      </Tabs>
    </Stack>
  );
}

function PendingTasks() {
  const { t } = useTranslation(["onboarding"]);
  const [stage, setStage] = useState<OnboardingStage | "all">("all");
  const query = useQuery({
    queryKey: ["onboarding", "pending"],
    queryFn: listPendingOnboardingTasks,
  });
  const items = (query.data ?? []).filter((item) => stage === "all" || item.stage === stage);
  const overdue = items.filter((item) => item.isOverdue).length;

  return (
    <Stack gap="md">
      <Group justify="space-between" wrap="wrap">
        <SegmentedControl
          value={stage}
          onChange={(value) => setStage(value as OnboardingStage | "all")}
          data={[
            { value: "all", label: t("onboarding:filters.all") },
            ...ONBOARDING_STAGES.map((value) => ({
              value,
              label: t(`onboarding:stages.${value}`),
            })),
          ]}
        />
        {overdue > 0 && (
          <Text size="sm" c="red" fw={600}>
            {t("onboarding:tasks.overdueCount", { count: overdue })}
          </Text>
        )}
      </Group>
      {query.isLoading && <Skeleton height={160} />}
      {query.isError && <Text c="red">{t("onboarding:loadError")}</Text>}
      {query.isSuccess && items.length === 0 && (
        <Card withBorder>
          <Group gap="sm">
            <ListChecks size={18} />
            <Text c="dimmed">{t("onboarding:tasks.noPending")}</Text>
          </Group>
        </Card>
      )}
      {items.length > 0 && <OnboardingTaskTable items={items} />}
    </Stack>
  );
}

function EmployeeChecklist() {
  const { t } = useTranslation(["onboarding", "common"]);
  const canManage = usePermission("onboarding.tasks.manage");
  const queryClient = useQueryClient();
  const [employee, setEmployee] = useState<PickedEmployee | null>(null);
  const [stage, setStage] = useState<OnboardingStage>("onboarding");
  const [referenceDate, setReferenceDate] = useState(toDateOnly(new Date()));

  const query = useQuery({
    queryKey: ["onboarding", "employee", employee?.id],
    queryFn: () => listEmployeeOnboardingTasks(employee!.id),
    enabled: employee !== null,
  });
  const createMutation = useMutation({
    mutationFn: () => createEmployeeChecklist(employee!.id, stage, referenceDate),
    onSuccess: () => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t("onboarding:checklist.created"),
      });
      void queryClient.invalidateQueries({ queryKey: ["onboarding"] });
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("onboarding:actionFailed")),
      }),
  });

  const items = query.data ?? [];

  return (
    <Stack gap="md">
      <EmployeePicker
        id="onboarding-employee"
        label={t("onboarding:fields.employee")}
        value={employee}
        onChange={setEmployee}
        allowClear
      />
      {employee && canManage && (
        <Card withBorder padding="md">
          <Stack gap="sm">
            <Text size="sm">{t("onboarding:checklist.createHint")}</Text>
            <Group align="flex-end" gap="sm" wrap="wrap">
              <SegmentedControl
                value={stage}
                onChange={(value) => setStage(value as OnboardingStage)}
                data={ONBOARDING_STAGES.map((value) => ({
                  value,
                  label: t(`onboarding:stages.${value}`),
                }))}
              />
              <Input.Wrapper
                label={t(
                  stage === "onboarding"
                    ? "onboarding:checklist.hireDate"
                    : "onboarding:checklist.exitDate"
                )}
              >
                <DatePicker
                  id="onboarding-reference-date"
                  value={referenceDate}
                  onChange={setReferenceDate}
                />
              </Input.Wrapper>
              <Button
                onClick={() => createMutation.mutate()}
                disabled={!referenceDate}
                loading={createMutation.isPending}
              >
                {t("onboarding:checklist.create")}
              </Button>
            </Group>
          </Stack>
        </Card>
      )}
      {employee && <EmployeeOnboardingCard employeeId={employee.id} />}
      {employee && query.isLoading && <Skeleton height={120} />}
      {employee && query.isError && <Text c="red">{t("onboarding:loadError")}</Text>}
      {employee && query.isSuccess && items.length === 0 && (
        <Text size="sm" c="dimmed">
          {t("onboarding:checklist.empty")}
        </Text>
      )}
      {items.length > 0 && <OnboardingTaskTable items={items} showEmployee={false} />}
    </Stack>
  );
}
