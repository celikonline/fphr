import React, { useCallback, useEffect, useReducer, useRef, useState } from "react";
import { ActivityIndicator, Alert, Keyboard, StyleSheet, View } from "react-native";
import { StatusBar } from "expo-status-bar";
import { OptionSheet } from "@/components/form/OptionSheet";
import { EmptyState, ErrorBanner, Screen, ScreenHeader } from "@/components/ui";
import { resolveStatusBarStyle } from "@/navigation/statusBarStyle";
import {
  approvalsService,
  type DecideTaskInput,
  type InboxTask,
} from "@/services/ApprovalsService";
import { initialRejectFlowState, rejectFlowReducer } from "@/screens/app/approvalsRejectFlow";
import { palette } from "@/theme/theme";
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

export default function ApprovalsScreen() {
  const [tasks, setTasks] = useState<InboxTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [decidingTaskId, setDecidingTaskId] = useState<string | null>(null);
  const [rejectFlow, dispatchRejectFlow] = useReducer(rejectFlowReducer, initialRejectFlowState);
  const [filter, setFilter] = useState<ApprovalFilter>("all");
  const [filterSheetVisible, setFilterSheetVisible] = useState(false);

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
      Alert.alert("Hata", "Talep reddedilemedi. Lütfen tekrar deneyin.");
    }
  }

  const header = <ScreenHeader title="Onaylarım" />;

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
  const counts = countByFilter(tasks);
  const visibleTasks = filterTasks(tasks, filter);

  return (
    <>
      <Screen header={header} refreshing={refreshing} onRefresh={() => void onRefresh()}>
        {error ? <ErrorBanner message={error} onRetry={() => void onRefresh()} /> : null}

        <ApprovalTypeSelect
          filter={filter}
          counts={counts}
          onPress={() => setFilterSheetVisible(true)}
        />

        {visibleTasks.length === 0 ? (
          <EmptyState icon="checkmark-done-outline" title="Onay bekleyen talep yok" />
        ) : (
          visibleTasks.map((task) => {
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

      <OptionSheet
        visible={filterSheetVisible}
        title="Tür"
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
    </>
  );
}

const styles = StyleSheet.create({
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
});
