import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge, Button, Card, Group, Modal, Skeleton, Stack, Table, Text, Textarea, Title } from "@mantine/core";
import { Inbox as InboxIcon } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  decideTask,
  listInbox,
  overrideDecideTask,
  type DecideTaskPayload,
  type InboxTaskDto,
} from "@/services/workflow.service";

const INBOX_QUERY_KEY = ["workflow", "inbox"] as const;

export default function WorkflowInboxPage() {
  const canView = usePermission("workflow.inbox.view");
  if (!canView) return <NoAccess />;
  return <WorkflowInbox />;
}

function WorkflowInbox() {
  const { t } = useTranslation(["workflow", "common"]);
  const canOverride = usePermission("workflow.request.override");
  const queryClient = useQueryClient();

  const inboxQuery = useQuery({ queryKey: INBOX_QUERY_KEY, queryFn: listInbox });
  const items = inboxQuery.data ?? [];

  const [decideTarget, setDecideTarget] = useState<{ task: InboxTaskDto; override: boolean } | null>(null);

  function refresh() {
    queryClient.invalidateQueries({ queryKey: INBOX_QUERY_KEY });
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={2} className="flex items-center gap-2">
          <InboxIcon size={24} /> {t("workflow:inbox.title")}
        </Title>
        <Text c="dimmed">{t("workflow:inbox.subtitle")}</Text>
      </div>

      <Card withBorder padding={0}>
        {inboxQuery.isLoading ? (
          <Stack gap="xs" p="md">
            {Array.from({ length: 4 }, (_, i) => (
              <Skeleton key={i} h={32} />
            ))}
          </Stack>
        ) : inboxQuery.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {getApiErrorMessage(inboxQuery.error, t("common:errorGeneric"))}
          </Text>
        ) : items.length === 0 ? (
          <Text size="sm" c="dimmed" ta="center" p="xl">
            {t("workflow:inbox.empty")}
          </Text>
        ) : (
          <Table.ScrollContainer minWidth={720}>
            <Table verticalSpacing="sm" horizontalSpacing="md" highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t("workflow:inbox.requestType", { defaultValue: "Talep türü" })}</Table.Th>
                  <Table.Th>{t("workflow:inbox.subject", { defaultValue: "Kayıt" })}</Table.Th>
                  <Table.Th>{t("workflow:inbox.requester")}</Table.Th>
                  <Table.Th>{t("workflow:inbox.stepColumn", { defaultValue: "Adım" })}</Table.Th>
                  <Table.Th />
                  <Table.Th ta="right">{t("common:actions")}</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {items.map((task) => (
                  <Table.Tr key={task.taskId}>
                    <Table.Td fw={500}>{t(`workflow:requestType.${task.requestType}`)}</Table.Td>
                    <Table.Td maw={320}>
                      <Text ff="monospace" size="xs" truncate>
                        {task.subjectId}
                      </Text>
                    </Table.Td>
                    <Table.Td>{task.requesterName ?? "—"}</Table.Td>
                    <Table.Td>{t("workflow:inbox.step", { number: task.stepNumber })}</Table.Td>
                    <Table.Td>
                      {task.isFallbackRoleTask && (
                        <Badge variant="outline" color="gray">
                          {t("workflow:inbox.fallbackRole")}
                        </Badge>
                      )}
                    </Table.Td>
                    <Table.Td>
                      <Group justify="flex-end" gap="xs" wrap="nowrap">
                        <Button type="button" size="xs" onClick={() => setDecideTarget({ task, override: false })}>
                          {t("workflow:inbox.decide")}
                        </Button>
                        {canOverride && (
                          <Button
                            type="button"
                            size="xs"
                            variant="default"
                            onClick={() => setDecideTarget({ task, override: true })}
                          >
                            {t("workflow:inbox.overrideDecide")}
                          </Button>
                        )}
                      </Group>
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        )}
      </Card>

      {decideTarget && (
        <DecideDialog
          task={decideTarget.task}
          override={decideTarget.override}
          onClose={() => setDecideTarget(null)}
          onDecided={refresh}
        />
      )}
    </div>
  );
}

interface DecideDialogProps {
  task: InboxTaskDto;
  override: boolean;
  onClose: () => void;
  onDecided: () => void;
}

function DecideDialog({ task, override, onClose, onDecided }: DecideDialogProps) {
  const { t } = useTranslation(["workflow", "common"]);
  const [comment, setComment] = useState("");

  const mutation = useMutation({
    mutationFn: (outcome: DecideTaskPayload["outcome"]) => {
      const payload: DecideTaskPayload = { outcome, comment: comment.trim() || undefined };
      return override ? overrideDecideTask(task.taskId, payload) : decideTask(task.taskId, payload);
    },
    onSuccess: (_data, outcome) => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t(outcome === "approved" ? "workflow:inbox.approveSuccess" : "workflow:inbox.rejectSuccess"),
      });
      onDecided();
      onClose();
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("common:errorGeneric")) }),
  });

  return (
    <Modal
      opened
      onClose={() => !mutation.isPending && onClose()}
      title={
        <Stack gap={2}>
          <Text fw={600}>{override ? t("workflow:inbox.overrideDecide") : t("workflow:inbox.decide")}</Text>
          <Text size="sm" c="dimmed">
            {t(`workflow:requestType.${task.requestType}`)} · {task.subjectId}
          </Text>
        </Stack>
      }
    >
      <Stack gap="md">
        {override && (
          <Text size="sm" c="dimmed">
            {t("workflow:inbox.overrideHint")}
          </Text>
        )}
        <Textarea
          id="decide-comment"
          label={t("workflow:inbox.comment")}
          placeholder={t("workflow:inbox.commentPlaceholder")}
          value={comment}
          onChange={(e) => setComment(e.currentTarget.value)}
          disabled={mutation.isPending}
          autosize
          minRows={3}
        />
        <Group justify="flex-end" gap="xs">
          <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button type="button" color="red" onClick={() => mutation.mutate("rejected")} disabled={mutation.isPending}>
            {t("workflow:inbox.reject")}
          </Button>
          <Button type="button" onClick={() => mutation.mutate("approved")} disabled={mutation.isPending}>
            {t("workflow:inbox.approve")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
