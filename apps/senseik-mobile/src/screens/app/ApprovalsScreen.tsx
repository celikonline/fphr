import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  Modal,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import {
  approvalsService,
  type DecideTaskInput,
  type InboxTask,
  type InboxTaskLeaveSummary,
} from "@/services/ApprovalsService";
import { formatDays } from "@/lib/formatters";
import { formatLeaveDateOnly } from "@/components/leave/LeaveRequestListItem";
import { humanizeLeaveTypeCode, leaveTypeDisplayName } from "@/lib/leaveTypeNames";

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

export default function ApprovalsScreen() {
  const [tasks, setTasks] = useState<InboxTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [decidingTaskId, setDecidingTaskId] = useState<string | null>(null);
  const [rejectingTaskId, setRejectingTaskId] = useState<string | null>(null);
  const [rejectReason, setRejectReason] = useState("");

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

  async function onRefresh() {
    setRefreshing(true);
    await loadAll();
    setRefreshing(false);
  }

  function startReject(taskId: string) {
    setRejectingTaskId(taskId);
    setRejectReason("");
  }

  function cancelReject() {
    setRejectingTaskId(null);
    setRejectReason("");
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

  async function confirmReject(taskId: string) {
    setDecidingTaskId(taskId);
    try {
      await approvalsService.decideTask(taskId, buildRejectDecision(rejectReason));
      setRejectingTaskId(null);
      setRejectReason("");
      await loadAll();
    } catch {
      Alert.alert("Hata", "Talep reddedilemedi. Lütfen tekrar deneyin.");
    } finally {
      setDecidingTaskId(null);
    }
  }

  if (loading) {
    return (
      <View style={styles.centered}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  return (
    <ScrollView
      style={styles.container}
      contentContainerStyle={styles.content}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void onRefresh()} />}
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
          const isDeciding = decidingTaskId === task.taskId;
          const isRejecting = rejectingTaskId === task.taskId;
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
                  onPress={() => startReject(task.taskId)}
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

              {/* B6: Android has no Alert.prompt, so a free-text (optional) reject reason needs its
                  own small modal - this doubles as the "are you sure" confirmation for this
                  irreversible decision. */}
              {isRejecting && (
                <Modal transparent animationType="fade" visible onRequestClose={cancelReject}>
                  <View style={styles.modalOverlay}>
                    <View style={styles.modalCard}>
                      <Text style={styles.modalTitle}>Talebi reddet</Text>
                      <Text style={styles.modalMessage}>
                        Bu talebi reddetmek istediğinize emin misiniz? Bu işlem geri alınamaz.
                      </Text>
                      <Text style={styles.fieldLabel}>Gerekçe (opsiyonel)</Text>
                      <TextInput
                        style={[styles.input, styles.textArea]}
                        value={rejectReason}
                        onChangeText={setRejectReason}
                        placeholder="Gerekçenizi yazın"
                        multiline
                        numberOfLines={3}
                      />
                      <View style={styles.actionRow}>
                        <TouchableOpacity
                          style={styles.secondaryButton}
                          onPress={cancelReject}
                          disabled={isDeciding}
                        >
                          <Text style={styles.secondaryButtonText}>Vazgeç</Text>
                        </TouchableOpacity>
                        <TouchableOpacity
                          style={[styles.rejectButton, isDeciding && styles.buttonDisabled]}
                          onPress={() => void confirmReject(task.taskId)}
                          disabled={isDeciding}
                        >
                          {isDeciding ? (
                            <ActivityIndicator color={colors.white} />
                          ) : (
                            <Text style={styles.actionButtonText}>Reddet</Text>
                          )}
                        </TouchableOpacity>
                      </View>
                    </View>
                  </View>
                </Modal>
              )}
            </View>
          );
        })
      )}
    </ScrollView>
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
