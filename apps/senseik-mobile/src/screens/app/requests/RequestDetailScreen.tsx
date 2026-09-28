import React, { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Alert, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { useNavigation, useRoute, type RouteProp } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useTranslation } from "react-i18next";
import i18n from "@/lib/i18n";
import {
  EmptyState,
  ErrorBanner,
  GlassCard,
  IconCircleButton,
  Screen,
  ScreenHeader,
  StatusPill,
  TextField,
} from "@/components/ui";
import { getApiErrorMessage } from "@/lib/apiError";
import { formatDate, formatDateTime } from "@/lib/formatters";
import {
  attachmentOpenErrorMessage,
  openLeaveAttachment,
  shouldIgnoreAttachmentTap,
} from "@/lib/attachmentDownload";
import { requestTypeLabel } from "@/screens/app/approvals/approvalLabels";
import { leaveService, type LeaveRequestAttachment } from "@/services/LeaveService";
import {
  workflowService,
  type ApprovalRequestStatusDetail,
  type ApprovalStepStatus,
  type RequestComment,
} from "@/services/WorkflowService";
import { palette, text, tones } from "@/theme/theme";
import type { AppStackParamList } from "@/types/navigation";
import {
  canSubmitComment,
  highlightedStepNumber,
  isCommentingAllowed,
  overallStatusLabelKey,
  overallStatusTone,
  STEP_VISUAL_TONES,
  stepVisualStatus,
} from "./requestTimeline";

type RequestDetailRouteProp = RouteProp<AppStackParamList, "RequestDetail">;
type RequestDetailNavigationProp = NativeStackNavigationProp<AppStackParamList, "RequestDetail">;

const STEP_DATE_FORMAT: Intl.DateTimeFormatOptions = {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
};

const COMMENT_DATE_FORMAT: Intl.DateTimeFormatOptions = {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
};

function formatStepDate(value: string): string {
  return formatDate(new Date(value), STEP_DATE_FORMAT);
}

function formatCommentDate(value: string): string {
  return formatDateTime(new Date(value), COMMENT_DATE_FORMAT);
}

/** `approverName` is best-effort (server didn't resolve it, or the policy hides it); a role-queue
 * step has no single assignee at all. */
function stepApproverText(step: ApprovalStepStatus): string {
  if (step.approverName) return step.approverName;
  if (step.isFallbackRole) return i18n.t("approvals:card.fallbackApprover");
  return i18n.t("requests:detail.hiddenApprover");
}

type StepRowProps = {
  step: ApprovalStepStatus;
  detail: ApprovalRequestStatusDetail;
  isLast: boolean;
};

/** One dot + connector of the vertical approval-chain timeline (tasarım notu: green/amber/coral dots). */
function StepRow({ step, detail, isLast }: StepRowProps) {
  const { t } = useTranslation("requests");
  const visual = stepVisualStatus(step, detail);
  const isCurrent = step.stepNumber === highlightedStepNumber(detail);
  const tone = tones[STEP_VISUAL_TONES[visual]];

  return (
    <View style={styles.stepRow}>
      <View style={styles.stepMarker}>
        <View style={[styles.stepDot, { backgroundColor: tone.fg }]} />
        {!isLast ? <View style={styles.stepLine} /> : null}
      </View>
      <View style={[styles.stepBody, isCurrent ? styles.stepBodyCurrent : null]}>
        <View style={styles.stepHeaderRow}>
          <Text style={styles.stepTitle}>{t("detail.step", { step: step.stepNumber })}</Text>
          {visual !== "notReached" ? (
            <StatusPill label={t(`detail.stepStatus.${visual}`)} tone={STEP_VISUAL_TONES[visual]} />
          ) : null}
        </View>
        <Text style={styles.stepApprover}>{stepApproverText(step)}</Text>
        {step.decidedAt ? (
          <Text style={styles.stepCaption}>{formatStepDate(step.decidedAt)}</Text>
        ) : null}
        {step.comment ? <Text style={styles.stepComment}>{step.comment}</Text> : null}
      </View>
    </View>
  );
}

type CommentRowProps = { comment: RequestComment };

function CommentRow({ comment }: CommentRowProps) {
  const { t } = useTranslation("requests");
  return (
    <GlassCard style={styles.commentCard}>
      <View style={styles.commentHeaderRow}>
        <Text style={styles.commentAuthor} numberOfLines={1}>
          {comment.authorName ?? t("detail.unknownAuthor")}
        </Text>
        <Text style={styles.commentDate}>{formatCommentDate(comment.createdAt)}</Text>
      </View>
      <Text style={styles.commentText}>{comment.text}</Text>
    </GlassCard>
  );
}

/**
 * FG-009: leave requests are the only kind with attachments today (`GET leave/me/requests/{id}`,
 * senseik-mobile-011) - loaded lazily once the status call resolves `requestType === "Leave"`.
 */
function LeaveAttachmentsSection({ leaveRequestId }: { leaveRequestId: string }) {
  const { t } = useTranslation("leave");
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);
  const [attachments, setAttachments] = useState<LeaveRequestAttachment[]>([]);
  const [openingDocumentId, setOpeningDocumentId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setLoadError(false);
    leaveService
      .getMyLeaveRequest(leaveRequestId)
      .then((detail) => {
        if (!cancelled) setAttachments(detail.attachments ?? []);
      })
      .catch(() => {
        if (!cancelled) setLoadError(true);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [leaveRequestId]);

  async function openAttachment(documentId: string) {
    if (shouldIgnoreAttachmentTap(openingDocumentId)) return;
    setOpeningDocumentId(documentId);
    try {
      await openLeaveAttachment(() =>
        leaveService.getMyAttachmentDownloadUrl(leaveRequestId, documentId)
      );
    } catch {
      Alert.alert(t("common:alerts.error"), attachmentOpenErrorMessage());
    } finally {
      setOpeningDocumentId(null);
    }
  }

  if (loading) {
    return (
      <ActivityIndicator size="small" color={palette.white} style={styles.attachmentsLoading} />
    );
  }
  if (loadError) {
    return <Text style={styles.attachmentsError}>{t("attachments.loadError")}</Text>;
  }
  if (attachments.length === 0) {
    return null;
  }

  return (
    <GlassCard style={styles.attachmentsCard}>
      <Text style={styles.attachmentsTitle}>{t("attachments.show")}</Text>
      {attachments.map((attachment) => (
        <TouchableOpacity
          key={attachment.documentId}
          style={styles.attachmentRow}
          onPress={() => void openAttachment(attachment.documentId)}
          disabled={openingDocumentId !== null}
          accessibilityRole="button"
          accessibilityLabel={attachment.fileName}
        >
          {openingDocumentId === attachment.documentId ? (
            <ActivityIndicator size="small" color={palette.white} />
          ) : (
            <Text style={styles.attachmentText}>{attachment.fileName}</Text>
          )}
        </TouchableOpacity>
      ))}
    </GlassCard>
  );
}

/**
 * FG-009: one approval request's detail - type/dates/amount (from the calling card, or a generic
 * type label once loaded), the step-by-step approval chain, and comments both parties can post.
 * Reached from a Taleplerim/Onaylarım card tap or an in-app notification deep link
 * (`lib/notificationNavigation.ts`).
 */
export default function RequestDetailScreen() {
  const { t } = useTranslation("requests");
  const navigation = useNavigation<RequestDetailNavigationProp>();
  const route = useRoute<RequestDetailRouteProp>();
  const insets = useSafeAreaInsets();
  const { approvalRequestId, summary } = route.params;

  const [status, setStatus] = useState<ApprovalRequestStatusDetail | null>(null);
  const [comments, setComments] = useState<RequestComment[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [commentText, setCommentText] = useState("");
  const [sendingComment, setSendingComment] = useState(false);
  const [commentError, setCommentError] = useState<string | null>(null);

  const loadAll = useCallback(async () => {
    setError(null);
    try {
      const [statusResult, commentsResult] = await Promise.all([
        workflowService.getStatus(approvalRequestId),
        workflowService.getComments(approvalRequestId),
      ]);
      setStatus(statusResult);
      setComments(commentsResult);
    } catch (loadErrorCause) {
      setError(getApiErrorMessage(loadErrorCause, t("detail.loadError")));
    }
  }, [approvalRequestId, t]);

  useEffect(() => {
    setLoading(true);
    void loadAll().finally(() => setLoading(false));
  }, [loadAll]);

  async function onRefresh() {
    setRefreshing(true);
    await loadAll();
    setRefreshing(false);
  }

  async function submitComment() {
    if (!canSubmitComment(commentText) || sendingComment) return;
    setSendingComment(true);
    setCommentError(null);
    try {
      await workflowService.addComment(approvalRequestId, commentText.trim());
      setCommentText("");
      setComments(await workflowService.getComments(approvalRequestId));
    } catch (sendErrorCause) {
      setCommentError(getApiErrorMessage(sendErrorCause, t("detail.commentSendError")));
    } finally {
      setSendingComment(false);
    }
  }

  const headerTitle =
    summary?.title ?? (status ? requestTypeLabel(status.requestType) : t("detail.title"));
  const header = (
    <ScreenHeader
      title={headerTitle}
      subtitle={summary?.dateLabel}
      onBack={() => navigation.goBack()}
    />
  );

  if (loading) {
    return (
      <Screen scroll={false} header={header}>
        <View style={styles.centered}>
          <ActivityIndicator size="large" color={palette.white} />
        </View>
      </Screen>
    );
  }

  if (error || !status) {
    return (
      <Screen scroll={false} header={header}>
        <ErrorBanner message={error ?? t("detail.loadError")} onRetry={() => void onRefresh()} />
      </Screen>
    );
  }

  const commentingAllowed = isCommentingAllowed(status.status);

  return (
    <>
      <Screen header={header} refreshing={refreshing} onRefresh={() => void onRefresh()}>
        <GlassCard style={styles.summaryCard}>
          <View style={styles.summaryHeaderRow}>
            <Text style={styles.summaryDetail}>{summary?.detail}</Text>
            <StatusPill
              label={t(overallStatusLabelKey(status.status), { ns: "leave" })}
              tone={overallStatusTone(status.status)}
            />
          </View>
          {summary?.description ? (
            <Text style={styles.summaryDescription}>{summary.description}</Text>
          ) : null}
        </GlassCard>

        {status.requestType === "Leave" ? (
          <LeaveAttachmentsSection leaveRequestId={status.subjectId} />
        ) : null}

        <Text style={styles.sectionTitle}>{t("detail.timelineTitle")}</Text>
        <GlassCard style={styles.timelineCard}>
          {status.steps.map((step, index) => (
            <StepRow
              key={step.stepNumber}
              step={step}
              detail={status}
              isLast={index === status.steps.length - 1}
            />
          ))}
        </GlassCard>

        <Text style={styles.sectionTitle}>{t("detail.commentsTitle")}</Text>
        {comments.length === 0 ? (
          <EmptyState icon="chatbubble-outline" title={t("detail.commentsEmpty")} />
        ) : (
          comments.map((comment) => <CommentRow key={comment.id} comment={comment} />)
        )}
        {!commentingAllowed ? (
          <Text style={styles.commentsClosed}>{t("detail.commentsClosed")}</Text>
        ) : null}
      </Screen>

      {commentingAllowed ? (
        <View style={[styles.composer, { paddingBottom: insets.bottom + 12 }]}>
          <View style={styles.composerField}>
            <TextField
              label={t("detail.commentPlaceholder")}
              value={commentText}
              onChangeText={setCommentText}
              placeholder={t("detail.commentPlaceholder")}
              multiline
              editable={!sendingComment}
            />
            {commentError ? <Text style={styles.commentErrorText}>{commentError}</Text> : null}
          </View>
          {sendingComment ? (
            <ActivityIndicator size="small" color={palette.white} style={styles.composerSpinner} />
          ) : (
            <IconCircleButton
              icon="send"
              onPress={() => void submitComment()}
              accessibilityLabel={t("detail.commentSend")}
            />
          )}
        </View>
      ) : null}
    </>
  );
}

const styles = StyleSheet.create({
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  summaryCard: {
    marginHorizontal: 20,
    marginTop: 14,
    gap: 8,
  },
  summaryHeaderRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "flex-start",
    gap: 12,
  },
  summaryDetail: {
    ...text.heading,
    flex: 1,
  },
  summaryDescription: {
    ...text.bodyMuted,
  },
  sectionTitle: {
    ...text.section,
    marginHorizontal: 20,
    marginTop: 20,
    marginBottom: 8,
  },
  timelineCard: {
    marginHorizontal: 20,
    gap: 0,
  },
  stepRow: {
    flexDirection: "row",
    gap: 12,
  },
  stepMarker: {
    alignItems: "center",
    width: 16,
  },
  stepDot: {
    width: 12,
    height: 12,
    borderRadius: 6,
    marginTop: 4,
  },
  stepLine: {
    flex: 1,
    width: 2,
    backgroundColor: palette.line,
    marginTop: 4,
  },
  stepBody: {
    flex: 1,
    paddingBottom: 16,
    gap: 2,
  },
  stepBodyCurrent: {
    opacity: 1,
  },
  stepHeaderRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 8,
  },
  stepTitle: {
    ...text.body,
  },
  stepApprover: {
    ...text.bodyMuted,
  },
  stepCaption: {
    ...text.caption,
  },
  stepComment: {
    ...text.caption,
    marginTop: 2,
  },
  commentCard: {
    marginHorizontal: 20,
    marginBottom: 10,
    gap: 4,
  },
  commentHeaderRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 8,
  },
  commentAuthor: {
    ...text.body,
    flex: 1,
  },
  commentDate: {
    ...text.caption,
  },
  commentText: {
    ...text.bodyMuted,
  },
  commentsClosed: {
    ...text.caption,
    marginHorizontal: 20,
    marginTop: 8,
  },
  attachmentsLoading: {
    marginHorizontal: 20,
    marginTop: 10,
    alignSelf: "flex-start",
  },
  attachmentsError: {
    ...text.caption,
    color: tones.danger.fg,
    marginHorizontal: 20,
    marginTop: 10,
  },
  attachmentsCard: {
    marginHorizontal: 20,
    marginTop: 10,
    gap: 6,
  },
  attachmentsTitle: {
    ...text.caption,
  },
  attachmentRow: {
    minHeight: 44,
    justifyContent: "center",
  },
  attachmentText: {
    ...text.caption,
    color: palette.brandText,
    textDecorationLine: "underline",
  },
  composer: {
    flexDirection: "row",
    alignItems: "flex-end",
    gap: 10,
    paddingHorizontal: 20,
    paddingTop: 12,
    backgroundColor: palette.sheetBottom,
    borderTopWidth: 1,
    borderTopColor: palette.glassBorder,
  },
  composerField: {
    flex: 1,
  },
  composerSpinner: {
    marginBottom: 8,
  },
  commentErrorText: {
    ...text.caption,
    color: tones.danger.fg,
    marginTop: 4,
  },
});
