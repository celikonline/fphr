import React, { useCallback, useEffect, useReducer, useRef, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  Keyboard,
  KeyboardAvoidingView,
  Modal,
  Platform,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from "react-native";
import { StatusBar } from "expo-status-bar";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import {
  approvalsService,
  type DecideTaskInput,
  type InboxTask,
  type InboxTaskLeaveSummary,
} from "@/services/ApprovalsService";
import { leaveService, type LeaveRequestAttachment } from "@/services/LeaveService";
import { formatDays } from "@/lib/formatters";
import { formatLeaveDateOnly } from "@/components/leave/LeaveRequestListItem";
import { humanizeLeaveTypeCode, leaveTypeDisplayName } from "@/lib/leaveTypeNames";
import { initialRejectFlowState, rejectFlowReducer } from "@/screens/app/approvalsRejectFlow";
import {
  ATTACHMENT_OPEN_ERROR_MESSAGE,
  openLeaveAttachment,
  shouldIgnoreAttachmentTap,
} from "@/lib/attachmentDownload";

/** `RequestType` (Workflow.Domain) server value - same mapping as web `workflow.json`'daki `requestType.*`. */
const REQUEST_TYPE_LABELS: Record<string, string> = {
  Leave: "İzin",
  Advance: "Avans",
  Expense: "Harcama",
  Overtime: "Fazla mesai",
  Requisition: "Kadro talebi",
  AssetAssignment: "Zimmet",
};

export function requestTypeLabel(requestType: string): string {
  return REQUEST_TYPE_LABELS[requestType] ?? requestType;
}

/** Web `home.tsx`'teki `SUBMITTED_DATE` biçimiyle aynı: `gg.aa.yyyy`. */
const SUBMITTED_DATE_FORMAT: Intl.DateTimeFormatOptions = {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
};

function formatSubmittedAt(submittedAt: string): string {
  return new Intl.DateTimeFormat("tr-TR", SUBMITTED_DATE_FORMAT).format(new Date(submittedAt));
}

/** `TimeOnly` ("HH:mm:ss") → "HH:mm" for display. */
export function formatTime(value: string): string {
  return value.slice(0, 5);
}

/**
 * B5: the inbox summary's `leaveTypeName` can be a raw seeded resource key (e.g. "leave.type.annual")
 * just like `LeaveService`'s balances/requestable-types responses - translate it the same way. When
 * it's missing entirely, fall back to a humanized `leaveTypeCode` instead of a bare/raw string.
 */
export function leaveSummaryTypeLabel(
  leave: Pick<InboxTaskLeaveSummary, "leaveTypeName" | "leaveTypeCode">
): string {
  if (leave.leaveTypeName && leave.leaveTypeName.trim().length > 0) {
    return leaveTypeDisplayName(leave.leaveTypeName);
  }
  return humanizeLeaveTypeCode(leave.leaveTypeCode);
}

/** B6: the reject comment is optional now (a small confirmation Modal, not a mandatory reason field). */
export function buildRejectDecision(reason: string): DecideTaskInput {
  const comment = reason.trim();
  return comment.length > 0 ? { outcome: "rejected", comment } : { outcome: "rejected" };
}

/**
 * A1: fetches a Leave task's attachments so the approver knows a document was attached before
 * deciding. Never throws - missing `requesterEmployeeId` (older API build), 403 (no Manager scope
 * over this employee), 404 (request no longer exists) and any network error all just hide the row
 * instead of showing an error; the axios error object is never logged. Non-Leave tasks (Overtime,
 * Advance, ...) skip the request entirely - this is the only inbox detail endpoint mobile calls,
 * and only for the request type that actually has attachments.
 */
export async function loadLeaveTaskAttachments(
  task: Pick<InboxTask, "requestType" | "requesterEmployeeId" | "subjectId">
): Promise<LeaveRequestAttachment[]> {
  if (task.requestType !== "Leave" || !task.requesterEmployeeId) {
    return [];
  }
  try {
    const detail = await leaveService.getEmployeeLeaveRequest(
      task.requesterEmployeeId,
      task.subjectId
    );
    return detail.attachments ?? [];
  } catch {
    return [];
  }
}

/** A1: "📎 N belge: ad, ad" - `null` when there is nothing to show (no row rendered). Kept for callers that just need a plain summary string; the card itself now renders each name as its own tappable row (senseik-mobile-011) instead of this joined form. */
export function formatAttachmentsSummary(
  attachments: Pick<LeaveRequestAttachment, "fileName">[]
): string | null {
  if (attachments.length === 0) {
    return null;
  }
  return `📎 ${attachments.length} belge: ${attachments.map((a) => a.fileName).join(", ")}`;
}

/** senseik-mobile-011: "📎 N belge:" header shown above the tappable attachment rows. */
export function attachmentsCountLabel(count: number): string {
  return `📎 ${count} belge:`;
}

/**
 * A1: one card's attachment line - a dedicated component (not inline state in `ApprovalsScreen`) so
 * React's list reconciliation (`key={task.taskId}` on the card) gives "fetch once per card, cached"
 * for free: this only remounts (and re-fetches) when the task itself is a different task, not on
 * every inbox refresh of the same still-pending list.
 *
 * senseik-mobile-011: each attachment name is now its own tappable row - tapping fetches a fresh
 * 15-minute presigned download URL (manager-scoped, since this is the approver's inbox) and opens
 * it; `openingDocumentId` blocks a second tap while one download-url request/open is in flight.
 */
function LeaveTaskAttachmentsLine({ task }: { task: InboxTask }) {
  const [attachments, setAttachments] = useState<LeaveRequestAttachment[]>([]);
  const [openingDocumentId, setOpeningDocumentId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void loadLeaveTaskAttachments(task).then((result) => {
      if (!cancelled) {
        setAttachments(result);
      }
    });
    return () => {
      cancelled = true;
    };
    // `task.taskId` identifies the card; the other fields it reads (requestType/requesterEmployeeId/
    // subjectId) never change for the same task, so re-running only when the id changes is correct.
  }, [task.taskId]);

  async function openAttachment(documentId: string) {
    const requesterEmployeeId = task.requesterEmployeeId;
    if (shouldIgnoreAttachmentTap(openingDocumentId) || !requesterEmployeeId) {
      return;
    }
    setOpeningDocumentId(documentId);
    try {
      await openLeaveAttachment(() =>
        leaveService.getEmployeeAttachmentDownloadUrl(
          requesterEmployeeId,
          task.subjectId,
          documentId
        )
      );
    } catch {
      Alert.alert("Hata", ATTACHMENT_OPEN_ERROR_MESSAGE);
    } finally {
      setOpeningDocumentId(null);
    }
  }

  if (attachments.length === 0) {
    return null;
  }

  return (
    <View>
      <Text style={styles.summaryText}>{attachmentsCountLabel(attachments.length)}</Text>
      {attachments.map((attachment) => {
        const isOpening = openingDocumentId === attachment.documentId;
        return (
          <TouchableOpacity
            key={attachment.documentId}
            onPress={() => void openAttachment(attachment.documentId)}
            disabled={openingDocumentId !== null}
            style={styles.attachmentRow}
          >
            {isOpening ? (
              <ActivityIndicator size="small" color={colors.primary} />
            ) : (
              <Text style={styles.attachmentText}>{attachment.fileName}</Text>
            )}
          </TouchableOpacity>
        );
      })}
    </View>
  );
}

export default function ApprovalsScreen() {
  const [tasks, setTasks] = useState<InboxTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [decidingTaskId, setDecidingTaskId] = useState<string | null>(null);
  const [rejectFlow, dispatchRejectFlow] = useReducer(rejectFlowReducer, initialRejectFlowState);

  const loadAll = useCallback(async () => {
    setError(null);
    try {
      const result = await approvalsService.getInbox();
      setTasks(result);
    } catch {
      setError("Onay kutusu yüklenemedi. Lütfen tekrar deneyin.");
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void loadAll().finally(() => setLoading(false));
  }, [loadAll]);

  // B19: the Android hardware back button, while the reject Modal's TextInput has focus, closes the
  // keyboard AND fires the Modal's `onRequestClose` in the same press (RN's Modal wires the native
  // Dialog's back handling straight to `onRequestClose`, with no keyboard-awareness - see
  // `react-native/Libraries/Modal/Modal.js`). Tracking keyboard visibility ourselves lets the reducer
  // treat that first back press as "close the keyboard only" and require a second, separate press to
  // actually close the modal.
  useEffect(() => {
    const showSubscription = Keyboard.addListener("keyboardDidShow", () =>
      dispatchRejectFlow({ type: "KEYBOARD_SHOWN" })
    );
    const hideSubscription = Keyboard.addListener("keyboardDidHide", () =>
      dispatchRejectFlow({ type: "KEYBOARD_HIDDEN" })
    );
    return () => {
      showSubscription.remove();
      hideSubscription.remove();
    };
  }, []);

  // B18: after the reject Modal's native Android Dialog window closes, the tab screens' status bar
  // icons have been observed reverting to the light/unreadable style - the Dialog window can affect
  // the Activity's system-bar appearance flags directly at the native level, outside of the
  // `expo-status-bar` JS module's own merge-stack, so nothing re-triggers our declarative
  // `<StatusBar style="dark">` in `RootNavigator` (its prop never actually changes). Re-assert the
  // dark style imperatively the moment the reject flow returns to idle (the modal has fully closed).
  const previousRejectFlowTypeRef = useRef(rejectFlow.type);
  useEffect(() => {
    if (previousRejectFlowTypeRef.current !== "idle" && rejectFlow.type === "idle") {
      StatusBar.setStyle("dark");
    }
    previousRejectFlowTypeRef.current = rejectFlow.type;
  }, [rejectFlow.type]);

  async function onRefresh() {
    setRefreshing(true);
    await loadAll();
    setRefreshing(false);
  }

  async function approve(taskId: string) {
    setDecidingTaskId(taskId);
    try {
      await approvalsService.decideTask(taskId, { outcome: "approved" });
      await loadAll();
    } catch {
      Alert.alert("Hata", "Talep onaylanamadı. Lütfen tekrar deneyin.");
    } finally {
      setDecidingTaskId(null);
    }
  }

  // B6: irreversible decision - confirm before deciding instead of acting on the first tap.
  function confirmApprove(taskId: string) {
    Alert.alert(
      "Talebi onayla",
      "Bu talebi onaylamak istediğinize emin misiniz? Bu işlem geri alınamaz.",
      [
        { text: "Vazgeç", style: "cancel" },
        { text: "Onayla", onPress: () => void approve(taskId) },
      ]
    );
  }

  // B19: closing via the hardware back button and closing via "Vazgeç" both go through the reducer,
  // so both fully reset the reject state the same way - there is no separate ad-hoc reset path left.
  function handleRequestClose() {
    if (rejectFlow.type === "rejecting" && rejectFlow.keyboardVisible) {
      Keyboard.dismiss();
    }
    dispatchRejectFlow({ type: "REQUEST_CLOSE" });
  }

  async function submitReject() {
    if (rejectFlow.type !== "rejecting") {
      return;
    }
    const { taskId, reason } = rejectFlow;
    dispatchRejectFlow({ type: "SUBMIT_START" });
    try {
      await approvalsService.decideTask(taskId, buildRejectDecision(reason));
      dispatchRejectFlow({ type: "SUBMIT_SUCCESS" });
      await loadAll();
    } catch {
      dispatchRejectFlow({ type: "SUBMIT_ERROR" });
      Alert.alert("Hata", "Talep reddedilemedi. Lütfen tekrar deneyin.");
    }
  }

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  const isRejectModalOpen = rejectFlow.type !== "idle";
  const isRejectDeciding = rejectFlow.type === "deciding";
  const rejectReasonValue = rejectFlow.type === "idle" ? "" : rejectFlow.reason;

  return (
    <>
      <ScrollView
        style={styles.container}
        contentContainerStyle={styles.content}
        refreshControl={
          <RefreshControl refreshing={refreshing} onRefresh={() => void onRefresh()} />
        }
      >
        <Text style={styles.title}>Onaylarım</Text>

        {error && (
          <View style={styles.errorBanner}>
            <Text style={styles.errorText}>{error}</Text>
          </View>
        )}

        {tasks.length === 0 ? (
          <Text style={styles.emptyText}>Onayınızı bekleyen talep yok.</Text>
        ) : (
          tasks.map((task) => {
            const isDeciding =
              decidingTaskId === task.taskId ||
              (isRejectDeciding && rejectFlow.taskId === task.taskId);
            return (
              <View key={task.taskId} style={styles.card}>
                <View style={styles.requestHeader}>
                  <Text style={styles.cardLabel}>{requestTypeLabel(task.requestType)}</Text>
                  {task.isFallbackRoleTask && (
                    <View style={styles.fallbackBadge}>
                      <Text style={styles.fallbackBadgeText}>Yedek onaylayan</Text>
                    </View>
                  )}
                </View>
                <Text style={styles.cardSubtext}>
                  {task.requesterName ?? "Bilinmiyor"} · Adım {task.stepNumber}
                </Text>
                {task.submittedAt && (
                  <Text style={styles.cardSubtext}>{formatSubmittedAt(task.submittedAt)}</Text>
                )}

                {task.summary?.leave && (
                  <View style={styles.summaryBox}>
                    <Text style={styles.summaryText}>
                      {leaveSummaryTypeLabel(task.summary.leave)} ·{" "}
                      {formatLeaveDateOnly(task.summary.leave.startDate)} –{" "}
                      {formatLeaveDateOnly(task.summary.leave.endDate)} ·{" "}
                      {formatDays(task.summary.leave.days)} gün
                    </Text>
                    {task.summary.leave.description && (
                      <Text style={styles.summaryText}>{task.summary.leave.description}</Text>
                    )}
                    <LeaveTaskAttachmentsLine task={task} />
                  </View>
                )}

                {task.summary?.overtime && (
                  <View style={styles.summaryBox}>
                    <Text style={styles.summaryText}>
                      {formatLeaveDateOnly(task.summary.overtime.workDate)} ·{" "}
                      {formatTime(task.summary.overtime.startTime)} ·{" "}
                      {task.summary.overtime.durationMinutes} dk
                    </Text>
                    {task.summary.overtime.reason && (
                      <Text style={styles.summaryText}>{task.summary.overtime.reason}</Text>
                    )}
                  </View>
                )}

                <View style={styles.actionRow}>
                  <TouchableOpacity
                    style={[styles.rejectButton, isDeciding && styles.buttonDisabled]}
                    onPress={() =>
                      dispatchRejectFlow({ type: "START_REJECT", taskId: task.taskId })
                    }
                    disabled={isDeciding}
                  >
                    <Text style={styles.actionButtonText}>Reddet</Text>
                  </TouchableOpacity>
                  <TouchableOpacity
                    style={[styles.approveButton, isDeciding && styles.buttonDisabled]}
                    onPress={() => confirmApprove(task.taskId)}
                    disabled={isDeciding}
                  >
                    {isDeciding ? (
                      <ActivityIndicator color={colors.white} />
                    ) : (
                      <Text style={styles.actionButtonText}>Onayla</Text>
                    )}
                  </TouchableOpacity>
                </View>
              </View>
            );
          })
        )}
      </ScrollView>

      {/* B6: Android has no Alert.prompt, so a free-text (optional) reject reason needs its own
          small modal - this doubles as the "are you sure" confirmation for this irreversible
          decision. B19: a single Modal instance kept mounted for the whole screen (toggled via
          `visible`, not mounted/unmounted per task row) instead of one-per-task-row - this avoids
          repeatedly constructing/tearing down the underlying native Android Dialog, which is a
          plausible source of the leftover-touch-blocking-overlay symptom reported after the
          back-button interaction. */}
      <Modal
        transparent
        animationType="fade"
        visible={isRejectModalOpen}
        onRequestClose={handleRequestClose}
      >
        <KeyboardAvoidingView
          style={styles.modalOverlay}
          behavior={Platform.OS === "ios" ? "padding" : undefined}
        >
          <View style={styles.modalCard}>
            <Text style={styles.modalTitle}>Talebi reddet</Text>
            <Text style={styles.modalMessage}>
              Bu talebi reddetmek istediğinize emin misiniz? Bu işlem geri alınamaz.
            </Text>
            <Text style={styles.fieldLabel}>Gerekçe (opsiyonel)</Text>
            <TextInput
              style={[styles.input, styles.textArea]}
              value={rejectReasonValue}
              onChangeText={(reason) => dispatchRejectFlow({ type: "SET_REASON", reason })}
              placeholder="Gerekçenizi yazın"
              multiline
              numberOfLines={3}
              editable={!isRejectDeciding}
            />
            <View style={styles.actionRow}>
              <TouchableOpacity
                style={styles.secondaryButton}
                onPress={() => dispatchRejectFlow({ type: "CANCEL" })}
                disabled={isRejectDeciding}
              >
                <Text style={styles.secondaryButtonText}>Vazgeç</Text>
              </TouchableOpacity>
              <TouchableOpacity
                style={[styles.rejectButton, isRejectDeciding && styles.buttonDisabled]}
                onPress={() => void submitReject()}
                disabled={isRejectDeciding}
              >
                {isRejectDeciding ? (
                  <ActivityIndicator color={colors.white} />
                ) : (
                  <Text style={styles.actionButtonText}>Reddet</Text>
                )}
              </TouchableOpacity>
            </View>
          </View>
        </KeyboardAvoidingView>
      </Modal>
    </>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.background,
  },
  content: {
    padding: spacing.xl,
    paddingBottom: spacing.xxxl,
  },
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: colors.background,
  },
  title: {
    ...typography.h3,
    color: colors.text,
    marginBottom: spacing.lg,
  },
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm,
  },
  cardLabel: {
    ...typography.bodySm,
    color: colors.textSecondary,
  },
  cardSubtext: {
    ...typography.bodyXs,
    color: colors.textTertiary,
    marginTop: spacing.xs,
  },
  emptyText: {
    ...typography.bodySm,
    color: colors.textTertiary,
  },
  errorBanner: {
    backgroundColor: "#fdecea",
    borderRadius: 8,
    padding: spacing.md,
    marginBottom: spacing.md,
  },
  errorText: {
    ...typography.bodySm,
    color: colors.error,
  },
  requestHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  fallbackBadge: {
    borderRadius: 12,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
    backgroundColor: colors.gray100,
  },
  fallbackBadgeText: {
    ...typography.caption,
    color: colors.textSecondary,
  },
  modalOverlay: {
    flex: 1,
    backgroundColor: "rgba(17, 24, 39, 0.5)",
    alignItems: "center",
    justifyContent: "center",
    padding: spacing.xl,
  },
  modalCard: {
    width: "100%",
    backgroundColor: colors.white,
    borderRadius: 12,
    padding: spacing.lg,
  },
  modalTitle: {
    ...typography.h4,
    color: colors.text,
  },
  modalMessage: {
    ...typography.bodySm,
    color: colors.textSecondary,
    marginTop: spacing.xs,
  },
  summaryBox: {
    backgroundColor: colors.gray50,
    borderRadius: 8,
    padding: spacing.sm,
    marginTop: spacing.sm,
  },
  summaryText: {
    ...typography.bodyXs,
    color: colors.textSecondary,
  },
  attachmentRow: {
    paddingVertical: spacing.xs,
  },
  attachmentText: {
    ...typography.bodyXs,
    color: colors.primary,
    textDecorationLine: "underline",
  },
  fieldLabel: {
    ...typography.caption,
    color: colors.textSecondary,
    marginTop: spacing.md,
    marginBottom: spacing.xs,
  },
  input: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 8,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    ...typography.body,
    color: colors.text,
    backgroundColor: colors.white,
  },
  textArea: {
    minHeight: 72,
    textAlignVertical: "top",
  },
  actionRow: {
    flexDirection: "row",
    gap: spacing.sm,
    marginTop: spacing.md,
  },
  approveButton: {
    flex: 1,
    backgroundColor: colors.success,
    borderRadius: 8,
    paddingVertical: spacing.sm,
    alignItems: "center",
  },
  rejectButton: {
    flex: 1,
    backgroundColor: colors.error,
    borderRadius: 8,
    paddingVertical: spacing.sm,
    alignItems: "center",
  },
  secondaryButton: {
    flex: 1,
    backgroundColor: colors.gray50,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 8,
    paddingVertical: spacing.sm,
    alignItems: "center",
  },
  secondaryButtonText: {
    ...typography.button,
    color: colors.textSecondary,
  },
  buttonDisabled: {
    opacity: 0.6,
  },
  actionButtonText: {
    ...typography.button,
    color: colors.white,
  },
});
