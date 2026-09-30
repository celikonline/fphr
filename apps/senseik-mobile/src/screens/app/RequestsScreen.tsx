import React, { useCallback, useState } from "react";
import { ActivityIndicator, Alert, StyleSheet, View } from "react-native";
import { useFocusEffect, useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { useTranslation } from "react-i18next";
import i18n from "@/lib/i18n";
import { EmptyState, ErrorBanner, IconCircleButton, Screen, ScreenHeader } from "@/components/ui";
import { RequestSheet } from "@/navigation/RequestSheet";
import { leaveService } from "@/services/LeaveService";
import { expenseService } from "@/services/ExpenseService";
import { attendanceService } from "@/services/AttendanceService";
import { workflowService, type MyApprovalRequestSummary } from "@/services/WorkflowService";
import { getApiErrorMessage } from "@/lib/apiError";
import { palette } from "@/theme/theme";
import type { AppStackParamList, RequestKind } from "@/types/navigation";
import { FilterChips } from "./requests/FilterChips";
import { RequestCardView } from "./requests/RequestCardView";
import { findApprovalRequestId } from "./requests/requestTimeline";
import {
  REQUEST_FILTERS,
  buildRequestCards,
  filterRequestCards,
  type RequestCard,
  type RequestFilter,
} from "./requests/requestCards";

/** Confirm/withdraw copy (i18n keys) per withdrawable kind (same texts the leave/overtime lists used). */
const WITHDRAW_FLOWS: Record<
  "leave" | "overtime" | "punchCorrection",
  { title: string; message: string; failure: string; run: (id: string) => Promise<void> }
> = {
  leave: {
    title: "leave:withdraw.confirmTitle",
    message: "leave:withdraw.confirmMessage",
    failure: "leave:withdraw.error",
    run: (id) => leaveService.withdrawLeaveRequest(id),
  },
  overtime: {
    title: "requests:withdrawOvertime.confirmTitle",
    message: "requests:withdrawOvertime.confirmMessage",
    failure: "requests:withdrawOvertime.error",
    run: (id) => leaveService.withdrawOvertimeRequest(id),
  },
  punchCorrection: {
    title: "requests:withdrawPunchCorrection.confirmTitle",
    message: "requests:withdrawPunchCorrection.confirmMessage",
    failure: "requests:withdrawPunchCorrection.error",
    run: (id) => attendanceService.withdrawPunchCorrection(id),
  },
};

function withdrawFlowFor(card: RequestCard) {
  return card.kind === "leave" || card.kind === "overtime" || card.kind === "punchCorrection"
    ? WITHDRAW_FLOWS[card.kind]
    : null;
}

function settledValue<T>(result: PromiseSettledResult<T[]>): T[] {
  return result.status === "fulfilled" ? result.value : [];
}

/**
 * Taleplerim: every own request (izin, fazla mesai, masraf, avans) as one list with a type filter.
 * New requests start from the "+" button (same sheet as the tab bar's FAB); the list reloads on
 * focus, so it is fresh after returning from NewRequest.
 */
export default function RequestsScreen() {
  const { t } = useTranslation("requests");
  const navigation = useNavigation<NativeStackNavigationProp<AppStackParamList>>();
  const [cards, setCards] = useState<RequestCard[]>([]);
  const [filter, setFilter] = useState<RequestFilter>("all");
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [withdrawingKey, setWithdrawingKey] = useState<string | null>(null);
  const [sheetVisible, setSheetVisible] = useState(false);
  // FG-009: subjectId -> approvalRequestId lookup for the card tap -> detail navigation. A failed
  // load just leaves cards non-tappable (kontrolcü notu) - never its own error banner.
  const [myApprovalRequests, setMyApprovalRequests] = useState<MyApprovalRequestSummary[]>([]);

  // Sources load independently: one failing list never hides the others.
  const loadAll = useCallback(async () => {
    const [leave, overtime, expense, advance, categories, punchCorrection, myRequests] =
      await Promise.allSettled([
        leaveService.getMyRequests(),
        leaveService.getMyOvertimeRequests(),
        expenseService.getMyExpenseClaims(),
        expenseService.getMyAdvanceRequests(),
        expenseService.getCategories(),
        attendanceService.getMyPunchCorrections(),
        workflowService.getMyRequests(),
      ]);
    setCards(
      buildRequestCards({
        leave: settledValue(leave),
        overtime: settledValue(overtime),
        expense: settledValue(expense),
        advance: settledValue(advance),
        categories: settledValue(categories),
        punchCorrection: settledValue(punchCorrection),
      })
    );
    setMyApprovalRequests(settledValue(myRequests));
    // Categories only name expense cards; their failure alone is not worth a banner.
    const failed = [leave, overtime, expense, advance, punchCorrection].some(
      (result) => result.status === "rejected"
    );
    setError(failed ? i18n.t("requests:list.partialLoadError") : null);
  }, []);

  useFocusEffect(
    useCallback(() => {
      void loadAll().finally(() => setLoading(false));
    }, [loadAll])
  );

  async function onRefresh() {
    setRefreshing(true);
    await loadAll();
    setRefreshing(false);
  }

  async function withdraw(card: RequestCard) {
    const flow = withdrawFlowFor(card);
    if (!flow) return;
    setWithdrawingKey(card.key);
    try {
      await flow.run(card.id);
      await loadAll();
    } catch (withdrawError) {
      Alert.alert(t("common:alerts.error"), getApiErrorMessage(withdrawError, t(flow.failure)));
    } finally {
      setWithdrawingKey(null);
    }
  }

  // H1: irreversible action - confirm before withdrawing instead of acting on the first tap.
  function confirmWithdraw(card: RequestCard) {
    const flow = withdrawFlowFor(card);
    if (!flow) return;
    Alert.alert(t(flow.title), t(flow.message), [
      { text: t("common:actions.cancel"), style: "cancel" },
      {
        text: t("leave:withdraw.action"),
        style: "destructive",
        onPress: () => void withdraw(card),
      },
    ]);
  }

  function startRequest(kind: RequestKind) {
    setSheetVisible(false);
    navigation.navigate("NewRequest", { kind });
  }

  // FG-009: the summary already loaded for the card is passed along so the detail screen's header
  // shows instantly, without waiting on a second network round-trip.
  function openRequestDetail(card: RequestCard, approvalRequestId: string) {
    navigation.navigate("RequestDetail", {
      approvalRequestId,
      summary: {
        title: card.title,
        dateLabel: card.dateLabel,
        detail: card.detail,
        description: card.description,
      },
    });
  }

  const header = (
    <ScreenHeader
      title={t("list.title")}
      right={
        <IconCircleButton
          icon="add"
          onPress={() => setSheetVisible(true)}
          accessibilityLabel={t("sheet.title")}
        />
      }
    />
  );
  const sheet = (
    <RequestSheet
      visible={sheetVisible}
      onClose={() => setSheetVisible(false)}
      onSelect={startRequest}
    />
  );

  if (loading) {
    return (
      <Screen scroll={false} header={header}>
        <View style={styles.centered}>
          <ActivityIndicator size="large" color={palette.white} />
        </View>
        {sheet}
      </Screen>
    );
  }

  const visibleCards = filterRequestCards(cards, filter);

  return (
    <Screen header={header} refreshing={refreshing} onRefresh={() => void onRefresh()}>
      <FilterChips
        options={REQUEST_FILTERS.map((option) => ({
          value: option.value,
          label: t(option.labelKey),
        }))}
        value={filter}
        onChange={setFilter}
      />
      <View style={styles.list}>
        {error ? <ErrorBanner message={error} onRetry={() => void onRefresh()} /> : null}
        {visibleCards.length === 0 ? (
          <EmptyState
            icon="document-text-outline"
            title={t("list.emptyTitle")}
            message={t("list.emptyMessage")}
            actionLabel={t("sheet.title")}
            onAction={() => (filter === "all" ? setSheetVisible(true) : startRequest(filter))}
          />
        ) : (
          visibleCards.map((card) => {
            const approvalRequestId = findApprovalRequestId(myApprovalRequests, card.id, card.kind);
            return (
              <RequestCardView
                key={card.key}
                card={card}
                isWithdrawing={withdrawingKey === card.key}
                onWithdraw={() => confirmWithdraw(card)}
                onPress={
                  approvalRequestId ? () => openRequestDetail(card, approvalRequestId) : undefined
                }
              />
            );
          })
        )}
      </View>
      {sheet}
    </Screen>
  );
}

const styles = StyleSheet.create({
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  list: {
    paddingTop: 14,
    gap: 4,
  },
});
