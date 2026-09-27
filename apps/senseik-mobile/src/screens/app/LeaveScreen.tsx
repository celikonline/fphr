import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  View,
} from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import {
  leaveService,
  type EmployeeLeaveBalances,
  type LeaveRequest,
  type RequestableLeaveType,
} from "@/services/LeaveService";
import { LeaveBalanceCard } from "@/components/leave/LeaveBalanceCard";
import { LeaveRequestForm } from "@/components/leave/LeaveRequestForm";
import { LeaveRequestListItem } from "@/components/leave/LeaveRequestListItem";
import { getApiErrorMessage } from "@/lib/apiError";

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

export default function LeaveScreen() {
  const [balances, setBalances] = useState<EmployeeLeaveBalances | null>(null);
  const [requests, setRequests] = useState<LeaveRequest[]>([]);
  const [requestableTypes, setRequestableTypes] = useState<RequestableLeaveType[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [withdrawingRequestId, setWithdrawingRequestId] = useState<string | null>(null);

  const loadAll = useCallback(async () => {
    setError(null);
    try {
      const [balancesResult, requestsResult, typesResult] = await Promise.all([
        leaveService.getMyBalances(),
        leaveService.getMyRequests(),
        leaveService.getMyRequestableLeaveTypes(),
      ]);
      setBalances(balancesResult);
      setRequests(requestsResult);
      setRequestableTypes(typesResult);
    } catch {
      setError("İzin bilgileri yüklenemedi. Lütfen tekrar deneyin.");
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

  async function withdrawRequest(requestId: string) {
    setWithdrawingRequestId(requestId);
    try {
      await leaveService.withdrawLeaveRequest(requestId);
      await loadAll();
    } catch (withdrawError) {
      Alert.alert(
        "Hata",
        getApiErrorMessage(withdrawError, "İzin talebi geri çekilemedi. Lütfen tekrar deneyin.")
      );
    } finally {
      setWithdrawingRequestId(null);
    }
  }

  // H1: irreversible action - confirm before withdrawing instead of acting on the first tap.
  function confirmWithdraw(requestId: string) {
    Alert.alert(
      "İzin talebini geri çek",
      "Bu izin talebini geri çekmek istediğinize emin misiniz? Bu işlem geri alınamaz.",
      [
        { text: "Vazgeç", style: "cancel" },
        { text: "Geri çek", style: "destructive", onPress: () => void withdrawRequest(requestId) },
      ]
    );
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
      <Text style={styles.title}>İzinlerim</Text>

      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      <Text style={styles.sectionTitle}>Bakiyelerim</Text>
      {balances && balances.balances.length > 0 ? (
        balances.balances.map((item) => (
          <LeaveBalanceCard key={item.leaveTypeCode + item.leaveTypeName} item={item} />
        ))
      ) : (
        <Text style={styles.emptyText}>Bakiye kaydı bulunamadı.</Text>
      )}

      <Text style={styles.sectionTitle}>Yeni izin talebi</Text>
      <View style={styles.card}>
        <LeaveRequestForm requestableTypes={requestableTypes} onSubmitted={loadAll} />
      </View>

      <Text style={styles.sectionTitle}>Taleplerim</Text>
      {requests.length === 0 ? (
        <Text style={styles.emptyText}>Henüz izin talebiniz yok.</Text>
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
  sectionTitle: {
    ...typography.h4,
    color: colors.text,
    marginTop: spacing.xl,
    marginBottom: spacing.sm,
  },
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm,
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
});
