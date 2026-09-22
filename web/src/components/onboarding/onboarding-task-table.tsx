import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle } from "lucide-react";
import { Badge, Button, Group, Modal, Stack, Table, Text, Textarea } from "@mantine/core";
import EmployeeNameLink from "@/components/employees/employee-name-link";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  completeOnboardingTask,
  waiveOnboardingTask,
  type OnboardingChecklistItem,
  type OnboardingTaskStatus,
} from "@/services/onboarding.service";

/** Server limit (OnboardingLimits.WaivedReasonMaxLength). */
const WAIVE_REASON_MAX_LENGTH = 500;

const STATUS_COLOR: Record<OnboardingTaskStatus, string> = {
  pending: "blue",
  completed: "green",
  waived: "gray",
};

/** Label for a responsible-role code; unknown (custom) codes are shown as stored. */
export function useRoleLabel() {
  const { t } = useTranslation(["onboarding"]);
  return (code: string) => t(`onboarding:roles.${code}`, { defaultValue: code });
}

interface OnboardingTaskTableProps {
  items: OnboardingChecklistItem[];
  /** Hide the employee column when the table already belongs to one employee. */
  showEmployee?: boolean;
}

/** Checklist table: due date (overdue flagged), task, responsible, status; complete / waive for pending rows. */
export default function OnboardingTaskTable({ items, showEmployee = true }: OnboardingTaskTableProps) {
  const { t, i18n } = useTranslation(["onboarding", "common"]);
  const canManage = usePermission("onboarding.tasks.manage");
  const queryClient = useQueryClient();
  const roleLabel = useRoleLabel();
  const [waiving, setWaiving] = useState<OnboardingChecklistItem | null>(null);
  const [reason, setReason] = useState("");

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ["onboarding"] });
  const onError = (error: unknown) =>
    toast({
      variant: "destructive",
      title: t("common:error"),
      description: getApiErrorMessage(error, t("onboarding:actionFailed")),
    });
  const completeMutation = useMutation({
    mutationFn: (id: string) => completeOnboardingTask(id),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("onboarding:tasks.completedToast") });
      refresh();
    },
    onError,
  });
  const waiveMutation = useMutation({
    mutationFn: ({ id, text }: { id: string; text: string }) => waiveOnboardingTask(id, text),
    onSuccess: () => {
      toast({ variant: "success", title: t("common:success"), description: t("onboarding:tasks.waivedToast") });
      setWaiving(null);
      refresh();
    },
    onError,
  });

  return (
    <>
      <Table.ScrollContainer minWidth={760}>
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>{t("onboarding:fields.dueDate")}</Table.Th>
              {showEmployee && <Table.Th>{t("onboarding:fields.employee")}</Table.Th>}
              <Table.Th>{t("onboarding:fields.task")}</Table.Th>
              <Table.Th>{t("onboarding:fields.stage")}</Table.Th>
              <Table.Th>{t("onboarding:fields.responsible")}</Table.Th>
              <Table.Th>{t("onboarding:fields.status")}</Table.Th>
              {canManage && <Table.Th />}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {items.map((item) => (
              <Table.Tr key={item.id}>
                <Table.Td>
                  <Group gap={4} wrap="nowrap">
                    {item.isOverdue && <AlertTriangle size={14} color="var(--mantine-color-red-6)" />}
                    <Text size="sm" c={item.isOverdue ? "red" : undefined} fw={item.isOverdue ? 600 : undefined}>
                      {formatDateOnly(item.dueDate, i18n.language)}
                    </Text>
                  </Group>
                </Table.Td>
                {showEmployee && (
                  <Table.Td>
                    <EmployeeNameLink id={item.employeeId} />
                  </Table.Td>
                )}
                <Table.Td>
                  <Group gap={6} wrap="nowrap">
                    <Text size="sm">{item.title}</Text>
                    {item.isMandatory && (
                      <Badge size="xs" color="red" variant="outline">
                        {t("onboarding:fields.mandatory")}
                      </Badge>
                    )}
                  </Group>
                  {item.waivedReason && (
                    <Text size="xs" c="dimmed">
                      {t("onboarding:tasks.waivedBecause", { reason: item.waivedReason })}
                    </Text>
                  )}
                </Table.Td>
                <Table.Td>{t(`onboarding:stages.${item.stage}`)}</Table.Td>
                <Table.Td>{roleLabel(item.responsibleRoleCode)}</Table.Td>
                <Table.Td>
                  <Badge color={STATUS_COLOR[item.status]} variant="light">
                    {t(`onboarding:statuses.${item.status}`)}
                  </Badge>
                </Table.Td>
                {canManage && (
                  <Table.Td>
                    {item.status === "pending" && (
                      <Group gap="xs" justify="flex-end" wrap="nowrap">
                        <Button
                          size="compact-sm"
                          variant="light"
                          loading={completeMutation.isPending && completeMutation.variables === item.id}
                          onClick={() => completeMutation.mutate(item.id)}
                        >
                          {t("onboarding:tasks.complete")}
                        </Button>
                        <Button
                          size="compact-sm"
                          variant="subtle"
                          color="gray"
                          onClick={() => {
                            setReason("");
                            setWaiving(item);
                          }}
                        >
                          {t("onboarding:tasks.waive")}
                        </Button>
                      </Group>
                    )}
                  </Table.Td>
                )}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>

      <Modal
        opened={waiving !== null}
        onClose={() => setWaiving(null)}
        title={t("onboarding:tasks.waiveTitle", { title: waiving?.title ?? "" })}
        centered
      >
        <Stack gap="md">
          <Textarea
            id="onboarding-waive-reason"
            label={t("onboarding:tasks.waiveReason")}
            value={reason}
            maxLength={WAIVE_REASON_MAX_LENGTH}
            autosize
            minRows={2}
            required
            onChange={(e) => setReason(e.currentTarget.value)}
          />
          <Group justify="flex-end" gap="sm">
            <Button variant="default" onClick={() => setWaiving(null)}>
              {t("common:cancel")}
            </Button>
            <Button
              disabled={!reason.trim()}
              loading={waiveMutation.isPending}
              onClick={() => waiving && waiveMutation.mutate({ id: waiving.id, text: reason.trim() })}
            >
              {t("onboarding:tasks.waive")}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </>
  );
}
