import React, { useCallback, useEffect, useReducer, useRef, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  Keyboard,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { StatusBar } from "expo-status-bar";
import { useTranslation } from "react-i18next";
import i18n from "@/lib/i18n";
import { OptionSheet } from "@/components/form/OptionSheet";
import { EmptyState, ErrorBanner, PrimaryButton, Screen, ScreenHeader } from "@/components/ui";
import { resolveStatusBarStyle } from "@/navigation/statusBarStyle";
import {
  approvalsService,
  type DecideTaskInput,
  type DecisionOutcome,
  type InboxTask,
} from "@/services/ApprovalsService";
import { initialRejectFlowState, rejectFlowReducer } from "@/screens/app/approvalsRejectFlow";
import {
  enterSelectionMode,
  exitSelectionMode,
  initialSelectionState,
  reconcileBulkResults,
  selectAllVisible,
  toggleTaskSelection,
  type SelectionState,
} from "@/screens/app/approvals/approvalSelection";
import { palette, radius, text } from "@/theme/theme";
import type { AppStackParamList } from "@/types/navigation";
import { ApprovalRejectSheet } from "./approvals/ApprovalRejectSheet";
import { ApprovalTaskCard } from "./approvals/ApprovalTaskCard";
import { ApprovalTypeSelect } from "./approvals/ApprovalTypeSelect";
import {
  approvalFilterOptions,
  approvalFilterSelectedText,
  countByFilter,
  filterTasks,
  type ApprovalFilter,
} from "./approvals/approvalFilters";

/** Re-exported so existing tests/imports keep working from this screen's path. */
export { approvalFilterOptions, approvalFilterSelectedText, countByFilter, filterTasks };
export type { ApprovalFilter };
export { formatTime, leaveSummaryTypeLabel, requestTypeLabel } from "./approvals/approvalLabels";
export {
  attachmentsCountLabel,
  formatAttachmentsSummary,
  loadLeaveTaskAttachments,
} from "./approvals/approvalAttachments";

/** B6: the reject comment is optional now (a small confirmation sheet, not a mandatory reason field). */
export function buildRejectDecision(reason: string): DecideTaskInput {
  const comment = reason.trim();
  return comment.length > 0 ? { outcome: "rejected", comment } : { outcome: "rejected" };
}

/** FG-008: the bulk reject flow reuses `rejectFlowReducer` (same idle/rejecting/deciding shape as
 * the single-task sheet) - it has no per-task id of its own, so this sentinel just fills the
 * reducer's `taskId` field; the actual ids being decided live in `selection.selectedIds`. */
const BULK_REJECT_SENTINEL_ID = "bulk";

type ApprovalsNavigationProp = NativeStackNavigationProp<AppStackParamList, "Approvals">;

type HeaderPillProps = { label: string; onPress: () => void };

/** "Geçmiş" / "Seç" / "İptal" - same hap kalıbı as `NotificationsScreen`'s `MarkAllReadPill`. */
function HeaderPill({ label, onPress }: HeaderPillProps) {
  return (
    <TouchableOpacity
      style={styles.headerPill}
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={label}
      hitSlop={{ top: 10, bottom: 10, left: 10, right: 10 }}
    >
      <Text style={styles.headerPillText}>{label}</Text>
    </TouchableOpacity>
  );
}

export default function ApprovalsScreen() {
  const { t } = useTranslation("approvals");
  const navigation = useNavigation<ApprovalsNavigationProp>();
  const insets = useSafeAreaInsets();
  const [tasks, setTasks] = useState<InboxTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [decidingTaskId, setDecidingTaskId] = useState<string | null>(null);
  const [rejectFlow, dispatchRejectFlow] = useReducer(rejectFlowReducer, initialRejectFlowState);
  const [filter, setFilter] = useState<ApprovalFilter>("all");
  const [filterSheetVisible, setFilterSheetVisible] = useState(false);

  // FG-008: toplu seçim - ayrı, saf bir durum (`approvalSelection.ts`), tekli red akışının
  // Android geri tuşu/klavye özel durumlarından bağımsız.
  const [selection, setSelection] = useState<SelectionState>(initialSelectionState);
  const [taskErrors, setTaskErrors] = useState<Record<string, string>>({});
  const [bulkApproving, setBulkApproving] = useState(false);
  const [bulkRejectFlow, dispatchBulkRejectFlow] = useReducer(
    rejectFlowReducer,
    initialRejectFlowState
  );

  const loadAll = useCallback(async () => {
    setError(null);
    try {
      const result = await approvalsService.getInbox();
      setTasks(result);
    } catch {
      setError(i18n.t("approvals:loadError"));
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void loadAll().finally(() => setLoading(false));
  }, [loadAll]);

  // B19: the Android hardware back button, while the reject sheet's text field has focus, closes the
  // keyboard AND fires the BottomSheet's `onRequestClose` in the same press (RN's Modal wires the
  // native Dialog's back handling straight to `onRequestClose`, with no keyboard-awareness - see
  // `react-native/Libraries/Modal/Modal.js`). Tracking keyboard visibility ourselves lets the reducer
  // treat that first back press as "close the keyboard only" and require a second, separate press to
  // actually close the sheet.
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

  // B18: after the reject sheet's native Android Dialog window closes, the status bar icons have
  // been observed reverting to the wrong style - the Dialog window can affect the Activity's
  // system-bar appearance flags directly at the native level, outside of the `expo-status-bar` JS
  // module's own merge-stack, so nothing re-triggers the declarative `<StatusBar>` in
  // `RootNavigator` (its prop never actually changes). Re-assert the app's style (always light on
  // the dark backdrop) imperatively the moment the reject flow returns to idle (sheet fully closed).
  const previousRejectFlowTypeRef = useRef(rejectFlow.type);
  useEffect(() => {
    if (previousRejectFlowTypeRef.current !== "idle" && rejectFlow.type === "idle") {
      StatusBar.setStyle(resolveStatusBarStyle());
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
      Alert.alert(t("common:alerts.error"), t("approve.error"));
    } finally {
      setDecidingTaskId(null);
    }
  }

  // B6: irreversible decision - confirm before deciding instead of acting on the first tap.
  function confirmApprove(taskId: string) {
    Alert.alert(t("approve.confirmTitle"), t("approve.confirmMessage"), [
      { text: t("common:actions.cancel"), style: "cancel" },
      { text: t("actions.approve"), onPress: () => void approve(taskId) },
    ]);
  }

  // B19: closing via the hardware back button/scrim and closing via "Vazgeç" both go through the
  // reducer, so both fully reset the reject state the same way - there is no separate ad-hoc reset
  // path left.
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
      Alert.alert(t("common:alerts.error"), t("reject.error"));
    }
  }

  function toggleSelectionMode() {
    if (selection.mode === "selecting") {
      setSelection(exitSelectionMode());
      setTaskErrors({});
    } else {
      setSelection(enterSelectionMode());
    }
  }

  /**
   * FG-008: sends the selected tasks through decide-bulk, then reconciles the partial result -
   * succeeded tasks drop out (via `loadAll`, the same server-truth refresh the single-task flow
   * uses), failed ones stay selected with their (already localized) error under the card.
   */
  async function decideSelectedBulk(outcome: DecisionOutcome, comment?: string) {
    const taskIds = selection.selectedIds;
    const results = await approvalsService.decideBulk({ taskIds, outcome, comment });
    const { failedTaskIds, errorsByTaskId } = reconcileBulkResults(results);
    setTaskErrors(errorsByTaskId);
    setSelection((current) =>
      failedTaskIds.length > 0 ? { ...current, selectedIds: failedTaskIds } : initialSelectionState
    );
    await loadAll();
  }

  async function approveSelectedBulk() {
    setBulkApproving(true);
    try {
      await decideSelectedBulk("approved");
    } catch {
      Alert.alert(t("common:alerts.error"), t("bulk.error"));
    } finally {
      setBulkApproving(false);
    }
  }

  function confirmBulkApprove() {
    const count = selection.selectedIds.length;
    Alert.alert(t("bulk.approveConfirmTitle"), t("bulk.approveConfirmMessage", { count }), [
      { text: t("common:actions.cancel"), style: "cancel" },
      { text: t("actions.approve"), onPress: () => void approveSelectedBulk() },
    ]);
  }

  async function submitBulkReject() {
    if (bulkRejectFlow.type !== "rejecting") {
      return;
    }
    const { reason } = bulkRejectFlow;
    dispatchBulkRejectFlow({ type: "SUBMIT_START" });
    try {
      await decideSelectedBulk("rejected", reason.trim());
      dispatchBulkRejectFlow({ type: "SUBMIT_SUCCESS" });
    } catch {
      dispatchBulkRejectFlow({ type: "SUBMIT_ERROR" });
      Alert.alert(t("common:alerts.error"), t("bulk.error"));
    }
  }

  const header = (
    <ScreenHeader
      title={t("title")}
      right={
        <View style={styles.headerActions}>
          <HeaderPill
            label={t("history.pill")}
            onPress={() => navigation.navigate("ApprovalHistory")}
          />
          <HeaderPill
            label={selection.mode === "selecting" ? t("selection.cancel") : t("selection.select")}
            onPress={toggleSelectionMode}
          />
        </View>
      }
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

  const isRejectDeciding = rejectFlow.type === "deciding";
  const rejectReasonValue = rejectFlow.type === "idle" ? "" : rejectFlow.reason;
  const isBulkRejectDeciding = bulkRejectFlow.type === "deciding";
  const bulkRejectReasonValue = bulkRejectFlow.type === "idle" ? "" : bulkRejectFlow.reason;
  const isSelecting = selection.mode === "selecting";
  const bulkBusy = bulkApproving || isBulkRejectDeciding;
  const counts = countByFilter(tasks);
  const visibleTasks = filterTasks(tasks, filter);
  const selectedCount = selection.selectedIds.length;

  return (
    <>
      <Screen
        header={header}
        refreshing={refreshing}
        onRefresh={() => void onRefresh()}
        scroll={!isSelecting || selectedCount === 0}
      >
        {error ? <ErrorBanner message={error} onRetry={() => void onRefresh()} /> : null}

        <ApprovalTypeSelect
          filter={filter}
          counts={counts}
          onPress={() => setFilterSheetVisible(true)}
        />

        {isSelecting && (
          <TouchableOpacity
            style={styles.selectAllRow}
            onPress={() =>
              setSelection((current) =>
                selectAllVisible(
                  current,
                  visibleTasks.map((task) => task.taskId)
                )
              )
            }
            accessibilityRole="button"
            accessibilityLabel={t("selection.selectAll")}
          >
            <Text style={styles.selectAllText}>{t("selection.selectAll")}</Text>
          </TouchableOpacity>
        )}

        {visibleTasks.length === 0 ? (
          <EmptyState icon="checkmark-done-outline" title={t("empty")} />
        ) : (
          visibleTasks.map((task) => {
            if (isSelecting) {
              return (
                <ApprovalTaskCard
                  key={task.taskId}
                  task={task}
                  isDeciding={false}
                  isApproving={false}
                  onApprove={() => {}}
                  onReject={() => {}}
                  selection={{
                    selected: selection.selectedIds.includes(task.taskId),
                    onToggle: () =>
                      setSelection((current) => toggleTaskSelection(current, task.taskId)),
                    disabled: bulkBusy,
                  }}
                  errorMessage={taskErrors[task.taskId]}
                />
              );
            }
            const isDeciding =
              decidingTaskId === task.taskId ||
              (isRejectDeciding && rejectFlow.taskId === task.taskId);
            return (
              <ApprovalTaskCard
                key={task.taskId}
                task={task}
                isDeciding={isDeciding}
                isApproving={decidingTaskId === task.taskId}
                onApprove={() => confirmApprove(task.taskId)}
                onReject={() => dispatchRejectFlow({ type: "START_REJECT", taskId: task.taskId })}
              />
            );
          })
        )}
      </Screen>

      {isSelecting && selectedCount > 0 && (
        <View style={[styles.bulkBar, { paddingBottom: insets.bottom + 16 }]}>
          <View style={styles.actionButton}>
            <PrimaryButton
              label={t("bulk.reject", { count: selectedCount })}
              variant="danger"
              disabled={bulkBusy}
              onPress={() =>
                dispatchBulkRejectFlow({ type: "START_REJECT", taskId: BULK_REJECT_SENTINEL_ID })
              }
            />
          </View>
          <View style={styles.actionButton}>
            <PrimaryButton
              label={t("bulk.approve", { count: selectedCount })}
              disabled={bulkBusy}
              loading={bulkApproving}
              onPress={confirmBulkApprove}
            />
          </View>
        </View>
      )}

      <OptionSheet
        visible={filterSheetVisible}
        title={t("typeLabel")}
        options={approvalFilterOptions(counts)}
        selected={filter}
        onSelect={setFilter}
        onClose={() => setFilterSheetVisible(false)}
      />

      <ApprovalRejectSheet
        visible={rejectFlow.type !== "idle"}
        reason={rejectReasonValue}
        isDeciding={isRejectDeciding}
        onChangeReason={(reason) => dispatchRejectFlow({ type: "SET_REASON", reason })}
        onCancel={() => dispatchRejectFlow({ type: "CANCEL" })}
        onSubmit={() => void submitReject()}
        onRequestClose={handleRequestClose}
      />

      <ApprovalRejectSheet
        visible={bulkRejectFlow.type !== "idle"}
        reason={bulkRejectReasonValue}
        isDeciding={isBulkRejectDeciding}
        onChangeReason={(reason) => dispatchBulkRejectFlow({ type: "SET_REASON", reason })}
        onCancel={() => dispatchBulkRejectFlow({ type: "CANCEL" })}
        onSubmit={() => void submitBulkReject()}
        onRequestClose={() => dispatchBulkRejectFlow({ type: "REQUEST_CLOSE" })}
        title={t("bulk.rejectTitle")}
        message={t("bulk.rejectMessage", { count: selectedCount })}
      />
    </>
  );
}

const styles = StyleSheet.create({
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  headerActions: {
    flexDirection: "row",
    gap: 8,
  },
  headerPill: {
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: radius.pill,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  headerPillText: {
    fontSize: 12,
    lineHeight: 16,
    color: palette.brandText,
  },
  selectAllRow: {
    marginHorizontal: 20,
    marginBottom: 12,
  },
  selectAllText: {
    ...text.body,
    color: palette.brandText,
  },
  bulkBar: {
    position: "absolute",
    left: 0,
    right: 0,
    bottom: 0,
    flexDirection: "row",
    gap: 10,
    paddingHorizontal: 20,
    paddingTop: 12,
    backgroundColor: palette.sheetBottom,
    borderTopWidth: 1,
    borderTopColor: palette.glassBorder,
  },
  actionButton: {
    flex: 1,
  },
});
