import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMediaQuery } from "@mantine/hooks";
import { Badge, Button, Card, Group, Modal, Skeleton, Stack, Text, Textarea, Title } from "@mantine/core";
import { Inbox as InboxIcon } from "lucide-react";
import { ApprovalCard, type ApprovalCardField } from "@/components/ui/approval-card";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDate, formatDateOnly } from "@/lib/dates";
import { useLeaveName, useDays } from "@/components/leave/leave-shared";
import { formatTimeOnly, useMinutes } from "@/components/leave/overtime-shared";
import {
  decideTask,
  listInbox,
  overrideDecideTask,
  type ApprovalRequestSummaryDto,
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
  const { t, i18n } = useTranslation(["workflow", "common", "leave"]);
  const canOverride = usePermission("workflow.request.override");
  const queryClient = useQueryClient();
  // ≤390px: decide/override buttons need a ≥40px touch target (Faz 1 tur 1 FE md.4).
  const isMobile = useMediaQuery("(max-width: 390px)");
  const touchButtonStyle = isMobile ? { minHeight: 40 } : undefined;
  const leaveName = useLeaveName();
  const days = useDays();
  const minutes = useMinutes();

  const inboxQuery = useQuery({ queryKey: INBOX_QUERY_KEY, queryFn: listInbox });
  const items = inboxQuery.data ?? [];

  const [decideTarget, setDecideTarget] = useState<{ task: InboxTaskDto; override: boolean } | null>(null);

  function refresh() {
    queryClient.invalidateQueries({ queryKey: INBOX_QUERY_KEY });
  }

  /**
   * senseik-047 D-01: request-type-specific fields from the summary projection - date+duration+reason for overtime,
   * date range+type+day count for leave - instead of the raw subjectId a manager can't act on.
   */
  function summaryFields(summary: ApprovalRequestSummaryDto | null | undefined): ApprovalCardField[] {
    if (!summary) return [];

    const requestDateField: ApprovalCardField = {
      label: t("workflow:inbox.requestDate"),
      value: formatDateOnly(summary.requestDate, i18n.language),
    };

    if (summary.overtime) {
      const { overtime } = summary;
      return [
        requestDateField,
        { label: t("workflow:inbox.startTime"), value: formatTimeOnly(overtime.startTime) },
        { label: t("workflow:inbox.duration"), value: minutes(overtime.durationMinutes) },
        { label: t("workflow:inbox.reason"), value: overtime.reason ?? "—" },
      ];
    }

    if (summary.leave) {
      const { leave } = summary;
      const typeLabel = leave.leaveTypeName ? leaveName(leave.leaveTypeName) : t(`leave:code.${leave.leaveTypeCode}`);
      const overlapCount = leave.teamOverlap?.count ?? 0;
      return [
        requestDateField,
        {
          label: t("workflow:inbox.dateRange"),
          value: `${formatDateOnly(leave.startDate, i18n.language)} – ${formatDateOnly(leave.endDate, i18n.language)}`,
        },
        { label: t("workflow:inbox.leaveType"), value: typeLabel },
        { label: t("workflow:inbox.days"), value: `${days(leave.days)} ${t("leave:days")}` },
        ...(leave.remainingBalance != null
          ? [{ label: t("workflow:inbox.remainingBalance"), value: `${days(leave.remainingBalance)} ${t("leave:days")}` }]
          : []),
        ...(leave.description?.trim() ? [{ label: t("workflow:inbox.description"), value: leave.description.trim() }] : []),
        ...(leave.teamOverlap && overlapCount > 0
          ? [
              {
                label: t("workflow:inbox.teamOverlap"),
                value: t("workflow:inbox.teamOverlapValue", {
                  count: overlapCount,
                  names: leave.teamOverlap.employees.map((employee) => employee.name).join(", "),
                }),
              },
            ]
          : []),
      ];
    }

    return [requestDateField];
  }

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={2} className="flex items-center gap-2">
          <InboxIcon size={24} /> {t("workflow:inbox.title")}
        </Title>
        <Text c="dimmed">{t("workflow:inbox.subtitle")}</Text>
      </div>

      {inboxQuery.isLoading ? (
        <Stack gap="xs">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} h={84} />
          ))}
        </Stack>
      ) : inboxQuery.isError ? (
        <Card withBorder padding="lg">
          <Text size="sm" c="red" ta="center">
            {getApiErrorMessage(inboxQuery.error, t("common:errorGeneric"))}
          </Text>
        </Card>
      ) : items.length === 0 ? (
        <Card withBorder padding="xl">
          <Text size="sm" c="dimmed" ta="center">
            {t("workflow:inbox.empty")}
          </Text>
        </Card>
      ) : (
        <Stack gap="sm">
          {items.map((task) => (
            <ApprovalCard
              key={task.taskId}
              title={t(`workflow:requestType.${task.requestType}`)}
              subtitle={task.subjectId}
              badges={
                task.isFallbackRoleTask && (
                  <Badge variant="outline" color="gray">
                    {t("workflow:inbox.fallbackRole")}
                  </Badge>
                )
              }
              fields={[
                { label: t("workflow:inbox.requester"), value: task.requesterName ?? "—" },
                { label: t("workflow:inbox.stepColumn", { defaultValue: "Adım" }), value: t("workflow:inbox.step", { number: task.stepNumber }) },
                ...(task.submittedAt
                  ? [
                      {
                        label: t("workflow:myRequests.submittedAt"),
                        value: formatDate(task.submittedAt, i18n.language),
                      },
                    ]
                  : []),
                ...summaryFields(task.summary),
              ]}
              actions={
                <>
                  <Button
                    type="button"
                    size={isMobile ? "sm" : "xs"}
                    style={touchButtonStyle}
                    onClick={() => setDecideTarget({ task, override: false })}
                  >
                    {t("workflow:inbox.decide")}
                  </Button>
                  {canOverride && (
                    <Button
                      type="button"
                      size={isMobile ? "sm" : "xs"}
                      style={touchButtonStyle}
                      variant="default"
                      onClick={() => setDecideTarget({ task, override: true })}
                    >
                      {t("workflow:inbox.overrideDecide")}
                    </Button>
                  )}
                </>
              }
            />
          ))}
        </Stack>
      )}

      {decideTarget && (
        <DecideDialog
          task={decideTarget.task}
          override={decideTarget.override}
          fields={summaryFields(decideTarget.task.summary)}
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
  /** senseik-047 D-01: the same request-type-specific fields shown on the inbox card, so the decide screen never falls back to a raw subjectId. */
  fields: ApprovalCardField[];
  onClose: () => void;
  onDecided: () => void;
}

function DecideDialog({ task, override, fields, onClose, onDecided }: DecideDialogProps) {
  const { t } = useTranslation(["workflow", "common"]);
  const [comment, setComment] = useState("");
  const [commentMissing, setCommentMissing] = useState(false);
  const isMobile = useMediaQuery("(max-width: 390px)");
  const touchButtonStyle = isMobile ? { minHeight: 40 } : undefined;

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

  /** A rejection needs a reason (server-enforced too); approving keeps the comment optional. */
  const decide = (outcome: DecideTaskPayload["outcome"]) => {
    if (outcome === "rejected" && !comment.trim()) {
      setCommentMissing(true);
      return;
    }
    mutation.mutate(outcome);
  };

  return (
    <Modal
      opened
      onClose={() => !mutation.isPending && onClose()}
      title={
        <Stack gap={2}>
          <Text fw={600}>{override ? t("workflow:inbox.overrideDecide") : t("workflow:inbox.decide")}</Text>
          <Text size="sm" c="dimmed">
            {t(`workflow:requestType.${task.requestType}`)}
          </Text>
        </Stack>
      }
    >
      <Stack gap="md">
        {fields.length > 0 && (
          <Card withBorder padding="sm">
            <Group gap="lg" wrap="wrap">
              {fields.map((field, index) => (
                <Stack gap={0} key={index}>
                  <Text size="xs" c="dimmed">
                    {field.label}
                  </Text>
                  <Text size="sm">{field.value}</Text>
                </Stack>
              ))}
            </Group>
          </Card>
        )}
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
          onChange={(e) => {
            setComment(e.currentTarget.value);
            setCommentMissing(false);
          }}
          error={commentMissing ? t("workflow:inbox.rejectCommentRequired") : undefined}
          disabled={mutation.isPending}
          autosize
          minRows={3}
        />
        <Group justify="flex-end" gap="xs" wrap={isMobile ? "wrap" : "nowrap"}>
          <Button type="button" variant="default" style={touchButtonStyle} onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button
            type="button"
            color="red"
            style={touchButtonStyle}
            onClick={() => decide("rejected")}
            disabled={mutation.isPending}
          >
            {t("workflow:inbox.reject")}
          </Button>
          <Button type="button" style={touchButtonStyle} onClick={() => decide("approved")} disabled={mutation.isPending}>
            {t("workflow:inbox.approve")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
