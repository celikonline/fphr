import React, { useCallback, useState } from "react";
import { ActivityIndicator, Alert, StyleSheet, View } from "react-native";
import { useFocusEffect, useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { useTranslation } from "react-i18next";
import {
  BottomSheet,
  EmptyState,
  ErrorBanner,
  PrimaryButton,
  Screen,
  ScreenHeader,
  SectionHeader,
} from "@/components/ui";
import {
  leaveService,
  type LeaveBalanceItem,
  type LeaveRequest,
  type RequestableLeaveType,
} from "@/services/LeaveService";
import { sortBalancesAnnualFirst } from "@/services/HomeFeedService";
import { LeaveBalanceCard } from "@/components/leave/LeaveBalanceCard";
import { LeaveRequestListItem } from "@/components/leave/LeaveRequestListItem";
import { getApiErrorMessage } from "@/lib/apiError";
import i18n from "@/lib/i18n";
import { palette } from "@/theme/theme";
import type { AppStackParamList } from "@/types/navigation";
import { BalanceCarousel } from "./home/BalanceCarousel";

/** H1: only a request still awaiting a decision can be withdrawn (`POST .../requests/{id}/withdraw`). */
export function canWithdrawLeaveRequest(request: Pick<LeaveRequest, "status">): boolean {
  return request.status === "pending";
}

/**
 * B21: the "Ekler" (attachments) link used to show on every card, including hourly/annual/etc.
 * requests that can never have a document - misleading, since most turned out empty on tap. The
 * `/leave/me/requests` list DTO has no `attachmentCount`/`hasAttachments` field to check directly
 * (confirmed against `LeaveRequestDto`, `LeaveRequestCore.cs:74-78`), so this matches the request's
 * `leaveTypeId` against the requestable-types catalog's `requiresDocument` instead.
 *
 * This is not a heuristic - it's exact for how attachments can exist today: `LeaveRequestForm.tsx`
 * only ever sends `documentIds` when `selectedType.requiresDocument` is true (`showDocuments =
 * !!selectedType?.requiresDocument`), and the same is true on web (`leave-request-dialog.tsx`'s
 * `showDocuments`/`documentIds` are both gated on `selected?.requiresDocument`). The one other way
 * to attach a file - `POST requests/{id}/attachments` (`attachMyLeaveDocument`, used by web's edit
 * dialog) - is itself only reachable when that same `showDocuments` gate is open, so a
 * non-`requiresDocument` request can never end up with an attachment from either client. A type no
 * longer in the (active-only) requestable-types list - e.g. deactivated since the request was made -
 * defaults to `false` (hide), consistent with "don't show a link that's overwhelmingly likely to be
 * empty".
 */
export function canLeaveRequestHaveAttachments(
  leaveTypeId: string,
  requestableTypes: Pick<RequestableLeaveType, "id" | "requiresDocument">[]
): boolean {
  return requestableTypes.some((type) => type.id === leaveTypeId && type.requiresDocument);
}

/**
 * İzinlerim: balance cards (same carousel as the home screen; a tap shows the period breakdown),
 * the "Yeni izin talebi" button (opens NewRequest) and the own request list with withdraw and
 * attachments. Reloads on focus, so a request sent from NewRequest shows up on return.
 */
export default function LeaveScreen() {
  const { t } = useTranslation("leave");
  const navigation = useNavigation<NativeStackNavigationProp<AppStackParamList>>();
  const [balances, setBalances] = useState<LeaveBalanceItem[]>([]);
  const [requests, setRequests] = useState<LeaveRequest[]>([]);
  const [requestableTypes, setRequestableTypes] = useState<RequestableLeaveType[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [withdrawingRequestId, setWithdrawingRequestId] = useState<string | null>(null);
  const [balanceDetail, setBalanceDetail] = useState<LeaveBalanceItem | null>(null);

  const loadAll = useCallback(async () => {
    try {
      const [balancesResult, requestsResult, typesResult] = await Promise.all([
        leaveService.getMyBalances(),
        leaveService.getMyRequests(),
        leaveService.getMyRequestableLeaveTypes(),
      ]);
      setBalances(sortBalancesAnnualFirst(balancesResult.balances));
      setRequests(requestsResult);
      setRequestableTypes(typesResult);
      setError(null);
    } catch {
      setError(i18n.t("leave:loadError"));
    }
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

  async function withdrawRequest(requestId: string) {
    setWithdrawingRequestId(requestId);
    try {
      await leaveService.withdrawLeaveRequest(requestId);
      await loadAll();
    } catch (withdrawError) {
      Alert.alert(t("common:alerts.error"), getApiErrorMessage(withdrawError, t("withdraw.error")));
    } finally {
      setWithdrawingRequestId(null);
    }
  }

  // H1: irreversible action - confirm before withdrawing instead of acting on the first tap.
  function confirmWithdraw(requestId: string) {
    Alert.alert(t("withdraw.confirmTitle"), t("withdraw.confirmMessage"), [
      { text: t("common:actions.cancel"), style: "cancel" },
      {
        text: t("withdraw.action"),
        style: "destructive",
        onPress: () => void withdrawRequest(requestId),
      },
    ]);
  }

  const header = <ScreenHeader title={t("title")} />;

  if (loading) {
    return (
      <Screen scroll={false} header={header}>
        <View style={styles.centered}>
          <ActivityIndicator size="large" color={palette.white} />
        </View>
      </Screen>
    );
  }

  if (error) {
    return (
      <Screen header={header} refreshing={refreshing} onRefresh={() => void onRefresh()}>
        <ErrorBanner message={error} onRetry={() => void onRefresh()} />
      </Screen>
    );
  }

  return (
    <Screen header={header} refreshing={refreshing} onRefresh={() => void onRefresh()}>
      <SectionHeader title={t("home:sections.balances")} />
      {balances.length > 0 ? (
        <BalanceCarousel items={balances} onPressItem={setBalanceDetail} />
      ) : (
        <EmptyState
          icon="wallet-outline"
          title={t("balancesEmpty")}
          message={t("balancesGuide")}
          actionLabel={t("getHelp")}
          onAction={() => navigation.navigate("Support")}
        />
      )}

      <View style={styles.newRequest}>
        <PrimaryButton
          label={t("newRequest")}
          icon="add"
          onPress={() => navigation.navigate("NewRequest", { kind: "leave" })}
        />
      </View>

      <SectionHeader title={t("myRequests")} />
      {requests.length === 0 ? (
        <EmptyState
          icon="sunny-outline"
          title={t("requestsEmpty")}
          message={t("requestsGuide")}
        />
      ) : (
        requests.map((request) => (
          <LeaveRequestListItem
            key={request.id}
            request={request}
            onWithdraw={
              canWithdrawLeaveRequest(request) ? () => confirmWithdraw(request.id) : undefined
            }
            isWithdrawing={withdrawingRequestId === request.id}
            showAttachments={canLeaveRequestHaveAttachments(request.leaveTypeId, requestableTypes)}
          />
        ))
      )}

      <BottomSheet
        visible={balanceDetail !== null}
        onClose={() => setBalanceDetail(null)}
        title={t("balanceDetail")}
      >
        {balanceDetail ? <LeaveBalanceCard item={balanceDetail} /> : null}
      </BottomSheet>
    </Screen>
  );
}

const styles = StyleSheet.create({
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  newRequest: {
    paddingHorizontal: 20,
    paddingTop: 8,
  },
});
