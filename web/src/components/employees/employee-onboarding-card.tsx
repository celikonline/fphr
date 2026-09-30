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
import { usePermission } from "@/components/permission-guard";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { toDateOnly } from "@/lib/dates";
import {
  getEmployeeCategoryProgress,
  listOnboardingTemplateSets,
  startOnboardingProcess,
} from "@/services/onboarding.service";

const FULL_PERCENT = 100;

/** Onboarding/offboarding on the employee: per-category progress and starting a named template set (E5). */
export function EmployeeOnboardingCard({ employeeId }: { employeeId: string }) {
  const { t } = useTranslation(["employees", "onboarding", "common"]);
  const canView = usePermission("onboarding.tasks.view");
  const canManage = usePermission("onboarding.tasks.manage");
  const queryClient = useQueryClient();
  const [setId, setSetId] = useState("");
  const [referenceDate, setReferenceDate] = useState(toDateOnly(new Date()));

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
      </Stack>
    </Card>
  );
}
