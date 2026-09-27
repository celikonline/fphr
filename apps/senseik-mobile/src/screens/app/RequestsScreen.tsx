import React, { useCallback, useState } from "react";
import { ActivityIndicator, Alert, StyleSheet, View } from "react-native";
import { useFocusEffect, useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { EmptyState, ErrorBanner, IconCircleButton, Screen, ScreenHeader } from "@/components/ui";
import { RequestSheet } from "@/navigation/RequestSheet";
import { leaveService } from "@/services/LeaveService";
import { expenseService } from "@/services/ExpenseService";
import { getApiErrorMessage } from "@/lib/apiError";
import { palette } from "@/theme/theme";
import type { AppStackParamList, RequestKind } from "@/types/navigation";
import { FilterChips } from "./requests/FilterChips";
import { RequestCardView } from "./requests/RequestCardView";
import {
  REQUEST_FILTERS,
  buildRequestCards,
  filterRequestCards,
  type RequestCard,
  type RequestFilter,
} from "./requests/requestCards";

/** Confirm/withdraw copy per withdrawable kind (same texts the leave/overtime lists used). */
const WITHDRAW_FLOWS: Record<
  "leave" | "overtime",
  { title: string; message: string; failure: string; run: (id: string) => Promise<void> }
> = {
  leave: {
    title: "İzin talebini geri çek",
    message: "Bu izin talebini geri çekmek istediğinize emin misiniz? Bu işlem geri alınamaz.",
    failure: "İzin talebi geri çekilemedi. Lütfen tekrar deneyin.",
    run: (id) => leaveService.withdrawLeaveRequest(id),
  },
  overtime: {
    title: "Fazla mesai talebini geri çek",
    message: "Bu talebi geri çekmek istediğinize emin misiniz? Bu işlem geri alınamaz.",
    failure: "Fazla mesai talebi geri çekilemedi. Lütfen tekrar deneyin.",
    run: (id) => leaveService.withdrawOvertimeRequest(id),
  },
};

function withdrawFlowFor(card: RequestCard) {
  return card.kind === "leave" || card.kind === "overtime" ? WITHDRAW_FLOWS[card.kind] : null;
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
  const navigation = useNavigation<NativeStackNavigationProp<AppStackParamList>>();
  const [cards, setCards] = useState<RequestCard[]>([]);
  const [filter, setFilter] = useState<RequestFilter>("all");
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [withdrawingKey, setWithdrawingKey] = useState<string | null>(null);
  const [sheetVisible, setSheetVisible] = useState(false);

  // Sources load independently: one failing list never hides the others.
  const loadAll = useCallback(async () => {
    const results = await Promise.allSettled([
      leaveService.getMyRequests(),
      leaveService.getMyOvertimeRequests(),
      expenseService.getMyExpenseClaims(),
      expenseService.getMyAdvanceRequests(),
      expenseService.getCategories(),
    ]);
    const [leave, overtime, expense, advance, categories] = results;
    setCards(
      buildRequestCards({
        leave: settledValue(leave),
        overtime: settledValue(overtime),
        expense: settledValue(expense),
        advance: settledValue(advance),
        categories: settledValue(categories),
      })
    );
    // Categories only name expense cards; their failure alone is not worth a banner.
    const failed = results.slice(0, 4).some((result) => result.status === "rejected");
    setError(failed ? "Bazı talepler yüklenemedi. Lütfen tekrar deneyin." : null);
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
      Alert.alert("Hata", getApiErrorMessage(withdrawError, flow.failure));
    } finally {
      setWithdrawingKey(null);
    }
  }

  // H1: irreversible action - confirm before withdrawing instead of acting on the first tap.
  function confirmWithdraw(card: RequestCard) {
    const flow = withdrawFlowFor(card);
    if (!flow) return;
    Alert.alert(flow.title, flow.message, [
      { text: "Vazgeç", style: "cancel" },
      { text: "Geri çek", style: "destructive", onPress: () => void withdraw(card) },
    ]);
  }

  function startRequest(kind: RequestKind) {
    setSheetVisible(false);
    navigation.navigate("NewRequest", { kind });
  }

  const header = (
    <ScreenHeader
      title="Taleplerim"
      right={
        <IconCircleButton
          icon="add"
          onPress={() => setSheetVisible(true)}
          accessibilityLabel="Yeni talep"
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
      <FilterChips options={REQUEST_FILTERS} value={filter} onChange={setFilter} />
      <View style={styles.list}>
        {error ? <ErrorBanner message={error} onRetry={() => void onRefresh()} /> : null}
        {visibleCards.length === 0 ? (
          <EmptyState
            icon="document-text-outline"
            title="Talep bulunamadı"
            message="Bu türde bir talebiniz yok."
            actionLabel="Yeni talep"
            onAction={() => (filter === "all" ? setSheetVisible(true) : startRequest(filter))}
          />
        ) : (
          visibleCards.map((card) => (
            <RequestCardView
              key={card.key}
              card={card}
              isWithdrawing={withdrawingKey === card.key}
              onWithdraw={() => confirmWithdraw(card)}
            />
          ))
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
