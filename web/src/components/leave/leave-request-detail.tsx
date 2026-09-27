import { useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import {
  Alert,
  Anchor,
  Badge,
  Button,
  Divider,
  Drawer,
  Group,
  Paper,
  Skeleton,
  Stack,
  Text,
  Textarea,
} from "@mantine/core";
import { CheckCircle2, Circle, CircleDot, XCircle } from "lucide-react";
import { FormModal } from "@/components/ui/form-modal";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, formatDateTime } from "@/lib/dates";
import {
  cancelLeaveRequest,
  getEmployeeLeaveRequestAttachmentDownloadUrl,
  getMyLeaveRequestAttachmentDownloadUrl,
  type LeaveApprovalStep,
  type LeaveRequest,
  type LeaveRequestAttachment,
  type LeaveRequestDetail,
} from "@/services/leave.service";
import { Field, LeaveStatusBadge, useDays } from "./leave-shared";
import { formatTimeOnly } from "./overtime-shared";
import { TeamOverlapCalendar } from "./team-overlap-calendar";

const CANCEL_REASON_MAX_LENGTH = 500;

interface LeaveRequestDetailSheetProps {
  requestId: string;
  /** Cache scope ("me" or the employee id). */
  scope: string;
  load: (id: string) => Promise<LeaveRequestDetail>;
  typeName: (request: LeaveRequest) => string;
  /** Actions for the loaded request (e.g. cancel, resubmit). */
  footer?: (detail: LeaveRequestDetail) => ReactNode;
  onClose: () => void;
}

/** Request detail drawer: dates, days, type, status, description, source, revision, cancellation and the approval chain. */
export function LeaveRequestDetailSheet({
  requestId,
  scope,
  load,
  typeName,
  footer,
  onClose,
}: LeaveRequestDetailSheetProps) {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const days = useDays();
  const query = useQuery({
    queryKey: ["leave", "request-detail", scope, requestId],
    queryFn: () => load(requestId),
    retry: false,
  });
  const detail = query.data;
  const date = (value: string) => formatDateOnly(value, i18n.language);

  return (
    <Drawer
      opened
      onClose={onClose}
      position="right"
      size="32rem"
      title={
        <Text component="span" fw={600} size="lg">
          {detail ? typeName(detail) : t("leave:detail.title")}
        </Text>
      }
      styles={{ body: { paddingBottom: 0 } }}
    >
      <Stack gap="lg">
        <Text size="sm" c="dimmed" mt={-8}>
          {detail ? detail.employeeName : t("leave:detail.description")}
        </Text>
        {query.isLoading ? (
          <Skeleton h={256} />
        ) : query.isError || !detail ? (
          <Text size="sm" c="red">
            {getApiErrorMessage(query.error, t("leave:genericError"))}
          </Text>
        ) : (
          <>
            <dl className="grid grid-cols-2 gap-4">
              <Field label={t("leave:requests.status")}>
                <LeaveStatusBadge status={detail.status} />
              </Field>
              <Field label={t("leave:requests.days")}>
                {days(detail.calculatedDays)} {t("leave:days")}
              </Field>
              <Field label={t("leave:requests.startDate")}>{date(detail.startDate)}</Field>
              <Field label={t("leave:requests.endDate")}>{date(detail.endDate)}</Field>
              {detail.halfDayPeriod && (
                <Field label={t("leave:detail.halfDayPeriod")}>
                  {t(`leave:request.halfDayPeriod.${detail.halfDayPeriod}`)}
                </Field>
              )}
              {detail.startTime && detail.endTime && (
                <Field label={t("leave:detail.timeRange")}>
                  {formatTimeOnly(detail.startTime)}–{formatTimeOnly(detail.endTime)}
                </Field>
              )}
              {detail.substituteEmployeeId && (
                <Field label={t("leave:detail.substitute")}>{detail.substituteName || "—"}</Field>
              )}
              <Field label={t("leave:requests.source")}>
                {t(`leave:requestSource.${detail.source}`)}
              </Field>
              <Field label={t("leave:detail.revision")}>{detail.revisionNo}</Field>
              <Field label={t("leave:detail.submittedAt")}>
                {formatDateTime(detail.submittedAt, i18n.language)}
              </Field>
              <Field label={t("leave:detail.decidedAt")}>
                {formatDateTime(detail.decidedAt, i18n.language)}
              </Field>
              {detail.remainingBalance !== null && detail.remainingBalance !== undefined && (
                <Field label={t("leave:detail.remainingBalance")}>
                  {days(detail.remainingBalance)} {t("leave:days")}
                </Field>
              )}
            </dl>
            <dl className="grid gap-4">
              <Field label={t("leave:requests.description")}>
                <Text span inherit style={{ whiteSpace: "pre-wrap" }}>
                  {detail.description || "—"}
                </Text>
              </Field>
              {detail.attachments !== null &&
                detail.attachments !== undefined &&
                detail.attachments.length > 0 && (
                  <Field label={t("leave:detail.attachments")}>
                    <ul className="flex flex-col gap-1">
                      {detail.attachments.map((attachment) => (
                        <li key={attachment.documentId}>
                          <AttachmentDownloadLink
                            requestId={detail.id}
                            scope={scope}
                            attachment={attachment}
                          />
                        </li>
                      ))}
                    </ul>
                  </Field>
                )}
              {detail.previousRequestId && (
                <Text size="xs" c="dimmed">
                  {t("leave:detail.resubmissionOf")}
                </Text>
              )}
            </dl>
            {(detail.status === "cancelled" || detail.cancelledAt) && (
              <Alert
                color="red"
                variant="light"
                title={t("leave:detail.cancelledAt", {
                  at: formatDateTime(detail.cancelledAt, i18n.language),
                })}
              >
                {detail.cancellationReason && (
                  <Text size="sm" c="dimmed" style={{ whiteSpace: "pre-wrap" }}>
                    {detail.cancellationReason}
                  </Text>
                )}
              </Alert>
            )}
            {detail.teamOverlap && detail.teamOverlap.count > 0 && (
              <TeamOverlapCalendar
                overlap={detail.teamOverlap}
                requestStart={detail.startDate}
                requestEnd={detail.endDate}
                locale={i18n.language}
              />
            )}
            <Divider />
            <ApprovalChain detail={detail} />
          </>
        )}
        <Group
          justify="flex-end"
          gap="xs"
          py="md"
          className="sticky bottom-0"
          bg="var(--mantine-color-body)"
          style={{ borderTop: "1px solid var(--mantine-color-default-border)" }}
        >
          {detail && footer?.(detail)}
          <Button type="button" variant="default" onClick={onClose}>
            {t("common:close")}
          </Button>
        </Group>
      </Stack>
    </Drawer>
  );
}

interface AttachmentDownloadLinkProps {
  requestId: string;
  /** Cache scope ("me" or the employee id) - decides which of the two download-url endpoints to call. */
  scope: string;
  attachment: LeaveRequestAttachment;
}

/** Denetim A5-belge: resolves a presigned download URL on click and opens it in a new tab; the URL itself is never logged. */
function AttachmentDownloadLink({ requestId, scope, attachment }: AttachmentDownloadLinkProps) {
  const { t } = useTranslation(["leave", "common"]);
  const mutation = useMutation({
    mutationFn: () =>
      scope === "me"
        ? getMyLeaveRequestAttachmentDownloadUrl(requestId, attachment.documentId)
        : getEmployeeLeaveRequestAttachmentDownloadUrl(scope, requestId, attachment.documentId),
    onSuccess: (result) => window.open(result.url, "_blank", "noopener,noreferrer"),
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("leave:genericError")),
      }),
  });

  return (
    <Anchor
      component="button"
      type="button"
      size="sm"
      disabled={mutation.isPending}
      onClick={() => mutation.mutate()}
    >
      {attachment.fileName}
    </Anchor>
  );
}

function StepIcon({ step, current }: { step: LeaveApprovalStep; current: boolean }) {
  if (step.status === "approved")
    return <CheckCircle2 size={20} color="var(--mantine-color-green-6)" />;
  if (step.status === "rejected") return <XCircle size={20} color="var(--mantine-color-red-6)" />;
  if (current) return <CircleDot size={20} color="var(--mantine-primary-color-filled)" />;
  return <Circle size={20} color="var(--mantine-color-dimmed)" />;
}

function ApprovalChain({ detail }: { detail: LeaveRequestDetail }) {
  const { t } = useTranslation(["leave"]);
  const approval = detail.approval;

  return (
    <section className="flex flex-col gap-3">
      <Group justify="space-between" gap="xs">
        <Text component="h3" size="sm" fw={600}>
          {t("leave:approval.title")}
        </Text>
        {approval && (
          <Badge variant="outline" color="gray">
            {t(`leave:approval.status.${approval.status}`, { defaultValue: approval.status })}
          </Badge>
        )}
      </Group>
      {!approval || approval.steps.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("leave:approval.none")}
        </Text>
      ) : (
        <ol className="flex flex-col gap-2">
          {[...approval.steps]
            .sort((a, b) => a.stepNumber - b.stepNumber)
            .map((step) => {
              const current =
                approval.status === "pending" && approval.currentStepNumber === step.stepNumber;
              const approver =
                step.approverName ??
                (step.fallbackRoleCode
                  ? t("leave:approval.role", { role: step.fallbackRoleCode })
                  : t("leave:approval.unassigned"));
              return (
                <Paper
                  key={step.stepNumber}
                  component="li"
                  withBorder
                  p="sm"
                  bg={current ? "var(--mantine-primary-color-light)" : undefined}
                  style={
                    current ? { borderColor: "var(--mantine-primary-color-filled)" } : undefined
                  }
                >
                  <Group align="flex-start" gap="sm" wrap="nowrap">
                    <StepIcon step={step} current={current} />
                    <div className="min-w-0 flex-1">
                      <Text size="sm" fw={500}>
                        {t("leave:approval.step", { number: step.stepNumber })} · {approver}
                      </Text>
                      <Text size="xs" c="dimmed">
                        {t(`leave:approval.stepStatus.${step.status}`, {
                          defaultValue: step.status,
                        })}
                        {" · "}
                        {step.isRequired
                          ? t("leave:approval.required")
                          : t("leave:approval.optional")}
                      </Text>
                      {step.comment && (
                        <Text size="xs" mt={4} style={{ whiteSpace: "pre-wrap" }}>
                          {step.comment}
                        </Text>
                      )}
                    </div>
                  </Group>
                </Paper>
              );
            })}
        </ol>
      )}
    </section>
  );
}

interface CancelLeaveRequestDialogProps {
  employeeId: string;
  request: LeaveRequest;
  typeName: (request: LeaveRequest) => string;
  onClose: () => void;
  onCancelled: () => void;
}

/** "İptal et": cancels a pending or approved request with a mandatory reason (leave.requests.cancel_approved). */
export function CancelLeaveRequestDialog({
  employeeId,
  request,
  typeName,
  onClose,
  onCancelled,
}: CancelLeaveRequestDialogProps) {
  const { t, i18n } = useTranslation(["leave", "common"]);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const mutation = useMutation({
    mutationFn: (value: string) => cancelLeaveRequest(employeeId, request.id, value),
    onSuccess: onCancelled,
    onError: (err) => setError(getApiErrorMessage(err, t("leave:genericError"))),
  });

  function submit(event: FormEvent) {
    event.preventDefault();
    if (!reason.trim()) return setError(t("leave:cancel.reasonRequired"));
    setError(null);
    mutation.mutate(reason.trim());
  }

  const busy = mutation.isPending;

  return (
    <FormModal
      opened
      onClose={onClose}
      size="28rem"
      title={t("leave:cancel.title")}
      closeOnClickOutside={false}
    >
      <form onSubmit={submit}>
        <Stack gap="md">
          <Text size="sm" c="dimmed">
            {t(
              request.status === "approved"
                ? "leave:cancel.descriptionApproved"
                : "leave:cancel.descriptionPending",
              {
                type: typeName(request),
                start: formatDateOnly(request.startDate, i18n.language),
                end: formatDateOnly(request.endDate, i18n.language),
              }
            )}
          </Text>
          <Textarea
            id="cancel-reason"
            label={t("leave:cancel.reason")}
            withAsterisk
            value={reason}
            maxLength={CANCEL_REASON_MAX_LENGTH}
            disabled={busy}
            autosize
            minRows={3}
            onChange={(e) => setReason(e.currentTarget.value)}
          />
          {error && (
            <Text size="sm" c="red" role="alert">
              {error}
            </Text>
          )}
          <Group justify="flex-end" gap="xs">
            <Button type="button" variant="default" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" color="red" loading={busy}>
              {busy ? t("common:saving") : t("leave:cancel.confirm")}
            </Button>
          </Group>
        </Stack>
      </form>
    </FormModal>
  );
}
