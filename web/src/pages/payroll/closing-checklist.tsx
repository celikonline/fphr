import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Badge,
  Button,
  Card,
  Group,
  Modal,
  Progress,
  Skeleton,
  Stack,
  Text,
  Textarea,
  Title,
} from "@mantine/core";
import { CheckCircle2, Circle, Play, ShieldOff, XCircle } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  completeClosingCheck,
  getClosingChecklist,
  runClosingChecks,
  waiveClosingCheck,
  type ClosingCheck,
  type ClosingStage,
} from "@/services/payroll.service";

const STAGES: ClosingStage[] = ["preparation", "calculation", "legal", "archive"];
const WAIVER_MAX_LENGTH = 500;

/**
 * "Kapanış kontrol listesi": the four-stage checklist of a payroll period. Automatic items are decided by rules over the
 * period's data (blocking ones stop the approval until fixed or waived with a reason); manual items are ticked off by a person.
 */
export function ClosingChecklist({ periodId }: { periodId: string }) {
  const { t } = useTranslation(["payroll", "common"]);
  const queryClient = useQueryClient();
  const canManage = usePermission("payroll.period.approve");
  const [waiving, setWaiving] = useState<ClosingCheck | null>(null);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const queryKey = ["payroll", "periods", periodId, "closing-checks"];
  const { data, isLoading } = useQuery({ queryKey, queryFn: () => getClosingChecklist(periodId) });

  const onError = (err: unknown) =>
    toast({
      variant: "destructive",
      title: t("common:error"),
      description: getApiErrorMessage(err, t("payroll:closing.error")),
    });
  const refresh = () => queryClient.invalidateQueries({ queryKey });

  const run = useMutation({
    mutationFn: () => runClosingChecks(periodId),
    onSuccess: refresh,
    onError,
  });
  const complete = useMutation({
    mutationFn: (check: ClosingCheck) => completeClosingCheck(periodId, check.code),
    onSuccess: refresh,
    onError,
  });
  const waive = useMutation({
    mutationFn: (check: ClosingCheck) => waiveClosingCheck(periodId, check.code, reason.trim()),
    onSuccess: async () => {
      await refresh();
      closeWaiver();
    },
    onError: (err) => setError(getApiErrorMessage(err, t("payroll:closing.error"))),
  });

  function closeWaiver() {
    setWaiving(null);
    setReason("");
    setError(null);
  }

  if (isLoading || !data) {
    return <Skeleton h={160} />;
  }

  const satisfied = data.filter((check) => check.isSatisfied).length;
  const openBlockers = data.filter(
    (check) => check.severity === "blocker" && !check.isSatisfied
  ).length;

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" align="flex-start" mb="sm" wrap="wrap">
        <div>
          <Title order={4}>{t("payroll:closing.title")}</Title>
          <Text size="xs" c="dimmed">
            {t("payroll:closing.subtitle")}
          </Text>
        </div>
        {canManage && (
          <Button
            variant="default"
            size="xs"
            leftSection={<Play size={14} />}
            loading={run.isPending}
            onClick={() => run.mutate()}
          >
            {t("payroll:closing.run")}
          </Button>
        )}
      </Group>

      <Group gap="sm" mb="xs">
        <Text size="sm" fw={600}>
          {t("payroll:closing.progress", { done: satisfied, total: data.length })}
        </Text>
        {openBlockers > 0 && (
          <Badge color="red" variant="light">
            {t("payroll:closing.openBlockers", { count: openBlockers })}
          </Badge>
        )}
      </Group>
      <Progress value={data.length === 0 ? 0 : (satisfied / data.length) * 100} mb="md" />

      <Stack gap="lg">
        {STAGES.map((stage) => {
          const items = data.filter((check) => check.stage === stage);
          if (items.length === 0) return null;
          return (
            <div key={stage}>
              <Text size="xs" tt="uppercase" fw={700} c="dimmed" mb={6}>
                {t(`payroll:closing.stage.${stage}`)} ({items.filter((c) => c.isSatisfied).length}/
                {items.length})
              </Text>
              <Stack gap={6}>
                {items.map((check) => (
                  <ChecklistRow
                    key={check.code}
                    check={check}
                    canManage={canManage}
                    completing={complete.isPending && complete.variables?.code === check.code}
                    onComplete={() => complete.mutate(check)}
                    onWaive={() => setWaiving(check)}
                  />
                ))}
              </Stack>
            </div>
          );
        })}
      </Stack>

      <Modal
        opened={waiving !== null}
        onClose={closeWaiver}
        title={t("payroll:closing.waiveTitle")}
        size="30rem"
      >
        {waiving && (
          <form
            onSubmit={(event) => {
              event.preventDefault();
              setError(null);
              waive.mutate(waiving);
            }}
          >
            <Text size="sm" fw={500} mb="xs">
              {t(`payroll:closing.items.${waiving.code}`)}
            </Text>
            <Textarea
              label={t("payroll:closing.waiveReason")}
              value={reason}
              onChange={(event) => setReason(event.currentTarget.value)}
              maxLength={WAIVER_MAX_LENGTH}
              minRows={3}
              autosize
              required
              data-autofocus
            />
            {error && (
              <Alert color="red" mt="sm">
                {error}
              </Alert>
            )}
            <Group justify="flex-end" mt="md">
              <Button variant="default" onClick={closeWaiver}>
                {t("common:cancel")}
              </Button>
              <Button type="submit" loading={waive.isPending} disabled={reason.trim().length === 0}>
                {t("payroll:closing.waive")}
              </Button>
            </Group>
          </form>
        )}
      </Modal>
    </Card>
  );
}

interface RowProps {
  check: ClosingCheck;
  canManage: boolean;
  completing: boolean;
  onComplete: () => void;
  onWaive: () => void;
}

function ChecklistRow({ check, canManage, completing, onComplete, onWaive }: RowProps) {
  const { t } = useTranslation(["payroll"]);
  const failed = check.status === "failed";
  const icon = check.isSatisfied ? (
    <CheckCircle2 size={18} color="var(--mantine-color-green-6)" />
  ) : failed ? (
    <XCircle size={18} color="var(--mantine-color-red-6)" />
  ) : (
    <Circle size={18} color="var(--mantine-color-gray-5)" />
  );

  return (
    <Group justify="space-between" align="flex-start" wrap="nowrap" gap="sm">
      <Group gap="xs" align="flex-start" wrap="nowrap" className="min-w-0">
        <span className="mt-0.5 shrink-0">{icon}</span>
        <div className="min-w-0">
          <Group gap={6} wrap="wrap">
            <Text size="sm">{t(`payroll:closing.items.${check.code}`)}</Text>
            <Badge size="xs" variant="light" color={check.kind === "automatic" ? "indigo" : "gray"}>
              {t(`payroll:closing.kind.${check.kind}`)}
            </Badge>
            {check.severity === "blocker" && (
              <Badge size="xs" variant="light" color="red">
                {t("payroll:closing.blocker")}
              </Badge>
            )}
            {check.status === "waived" && (
              <Badge size="xs" variant="light" color="yellow" leftSection={<ShieldOff size={10} />}>
                {t("payroll:closing.status.waived")}
              </Badge>
            )}
          </Group>
          {failed && (
            <Text size="xs" c="red.7">
              {check.detail
                ? t("payroll:closing.failedWithDetail", { detail: check.detail })
                : t("payroll:closing.failed")}
            </Text>
          )}
          {check.status === "passed" && check.kind === "manual" && check.detail && (
            <Text size="xs" c="dimmed">
              {check.detail}
            </Text>
          )}
          {check.status === "waived" && check.waiverReason && (
            <Text size="xs" c="dimmed">
              {t("payroll:closing.waivedBecause", { reason: check.waiverReason })}
            </Text>
          )}
        </div>
      </Group>
      {canManage && !check.isSatisfied && (
        <Group gap={4} wrap="nowrap">
          {check.kind === "manual" && (
            <Button size="compact-xs" variant="light" loading={completing} onClick={onComplete}>
              {t("payroll:closing.complete")}
            </Button>
          )}
          <Button size="compact-xs" variant="subtle" color="gray" onClick={onWaive}>
            {t("payroll:closing.waive")}
          </Button>
        </Group>
      )}
    </Group>
  );
}
