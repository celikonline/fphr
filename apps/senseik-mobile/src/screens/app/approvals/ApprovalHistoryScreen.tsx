import React, { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, FlatList, RefreshControl, StyleSheet, Text, View } from "react-native";
import { useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { useTranslation } from "react-i18next";
import i18n from "@/lib/i18n";
import { activeLocale } from "@/lib/formatters";
import {
  Avatar,
  EmptyState,
  ErrorBanner,
  GlassCard,
  Screen,
  ScreenHeader,
  StatusPill,
} from "@/components/ui";
import { OptionSheet } from "@/components/form/OptionSheet";
import {
  approvalsService,
  type ApprovalHistoryItem,
  type DecisionOutcome,
} from "@/services/ApprovalsService";
import { palette, text, type Tone } from "@/theme/theme";
import {
  approvalFilterOptions,
  countByFilter,
  filterTasks,
  type ApprovalFilter,
} from "./approvalFilters";
import { ApprovalTypeSelect } from "./ApprovalTypeSelect";
import { approvalCardHeading, approvalDetailRows } from "./approvalPresentation";
import type { AppStackParamList } from "@/types/navigation";

const PAGE_SIZE = 20;

const DECIDED_AT_FORMAT: Intl.DateTimeFormatOptions = {
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
};

function formatDecidedAt(decidedAt: string): string {
  return new Intl.DateTimeFormat(activeLocale(), DECIDED_AT_FORMAT).format(new Date(decidedAt));
}

const OUTCOME_TONE: Record<DecisionOutcome, Tone> = {
  approved: "success",
  rejected: "danger",
};

function outcomeLabel(outcome: DecisionOutcome): string {
  return i18n.t(`approvals:history.outcome.${outcome}`);
}

type HistoryCardProps = { item: ApprovalHistoryItem };

/** FG-008: read-only geçmiş kartı - "Onaylarım" kartıyla aynı başlık/özet mantığını (`approvalPresentation`) paylaşır, aksiyon satırı yerine karar `StatusPill` + tarih gösterir. */
function HistoryCard({ item }: HistoryCardProps) {
  const { t } = useTranslation("approvals");
  const { heading, subtitle } = approvalCardHeading(item);
  const detailRows = approvalDetailRows(item);
  const requesterName = item.requesterName ?? t("card.unknownRequester");

  return (
    <GlassCard style={styles.card}>
      <View style={styles.headerRow}>
        <Avatar name={requesterName} size={44} radius={14} />
        <View style={styles.headerTexts}>
          <Text style={styles.name} numberOfLines={1}>
            {requesterName}
          </Text>
          <Text style={styles.caption} numberOfLines={1}>
            {formatDecidedAt(item.decidedAt)}
          </Text>
        </View>
        <StatusPill label={outcomeLabel(item.outcome)} tone={OUTCOME_TONE[item.outcome]} />
      </View>

      <View style={styles.amountRow}>
        <View style={styles.amountTexts}>
          <Text style={styles.amount}>{heading}</Text>
          {subtitle ? <Text style={styles.typeCaption}>{subtitle}</Text> : null}
        </View>
      </View>

      {detailRows.length > 0 && (
        <>
          <View style={styles.divider} />
          {detailRows.map((row) => (
            <View key={row.label} style={styles.detailRow}>
              <Text style={styles.detailLabel}>{row.label}</Text>
              <Text style={styles.detailValue}>{row.value}</Text>
            </View>
          ))}
        </>
      )}

      {item.comment ? <Text style={styles.note}>{item.comment}</Text> : null}
    </GlassCard>
  );
}

/** FG-008: "Onaylarım" > "Geçmiş" - çağıranın kendi kararları, karar tarihine göre en yeniden eskiye, tür filtresiyle. */
export default function ApprovalHistoryScreen() {
  const { t } = useTranslation("approvals");
  const navigation = useNavigation<NativeStackNavigationProp<AppStackParamList>>();
  const [items, setItems] = useState<ApprovalHistoryItem[]>([]);
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<ApprovalFilter>("all");
  const [filterSheetVisible, setFilterSheetVisible] = useState(false);

  const loadFirstPage = useCallback(async () => {
    setError(null);
    try {
      const result = await approvalsService.getHistory({ page: 1, pageSize: PAGE_SIZE });
      setItems(result.items);
      setPage(1);
      setTotalCount(result.totalCount);
    } catch {
      setError(i18n.t("approvals:history.loadError"));
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void loadFirstPage().finally(() => setLoading(false));
  }, [loadFirstPage]);

  async function onRefresh() {
    setRefreshing(true);
    await loadFirstPage();
    setRefreshing(false);
  }

  async function loadMore() {
    if (loadingMore || loading || refreshing || items.length >= totalCount) {
      return;
    }
    setLoadingMore(true);
    try {
      const nextPage = page + 1;
      const result = await approvalsService.getHistory({ page: nextPage, pageSize: PAGE_SIZE });
      setItems((current) => [...current, ...result.items]);
      setPage(nextPage);
      setTotalCount(result.totalCount);
    } catch {
      // Same as NotificationsScreen's "load more": stays silent, pull-to-refresh retries.
    } finally {
      setLoadingMore(false);
    }
  }

  const counts = countByFilter(items);
  const visibleItems = filterTasks(items, filter);

  const header = <ScreenHeader title={t("history.title")} />;

  if (loading) {
    return (
      <Screen scroll={false} header={header}>
        <View style={styles.centered}>
          <ActivityIndicator size="large" color={palette.white} />
        </View>
      </Screen>
    );
  }

  return (
    <>
      <Screen scroll={false} header={header}>
        <View style={styles.flex}>
          {error ? <ErrorBanner message={error} onRetry={() => void onRefresh()} /> : null}

          <ApprovalTypeSelect
            filter={filter}
            counts={counts}
            onPress={() => setFilterSheetVisible(true)}
          />

          <FlatList
            data={visibleItems}
            keyExtractor={(item) => item.taskId}
            renderItem={({ item }) => <HistoryCard item={item} />}
            contentContainerStyle={styles.listContent}
            refreshControl={
              <RefreshControl
                refreshing={refreshing}
                onRefresh={() => void onRefresh()}
                tintColor={palette.white}
              />
            }
            onEndReachedThreshold={0.4}
            onEndReached={() => void loadMore()}
            ListEmptyComponent={
              <EmptyState
                icon="checkmark-done-outline"
                title={t("history.empty")}
                message={t("history.emptyGuide")}
                actionLabel={t("history.inbox")}
                onAction={() => navigation.navigate("Approvals")}
              />
            }
            ListFooterComponent={
              loadingMore ? <ActivityIndicator style={styles.footer} color={palette.white} /> : null
            }
          />
        </View>
      </Screen>

      <OptionSheet
        visible={filterSheetVisible}
        title={t("typeLabel")}
        options={approvalFilterOptions(counts)}
        selected={filter}
        onSelect={setFilter}
        onClose={() => setFilterSheetVisible(false)}
      />
    </>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  listContent: {
    paddingHorizontal: 20,
    paddingBottom: 12,
  },
  footer: {
    marginVertical: 12,
  },
  card: {
    marginBottom: 12,
    gap: 12,
  },
  headerRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
  },
  headerTexts: {
    flex: 1,
    minWidth: 0,
  },
  name: {
    ...text.heading,
  },
  caption: {
    ...text.caption,
    color: palette.brandText,
    marginTop: 2,
  },
  amountRow: {
    flexDirection: "row",
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: 12,
  },
  amountTexts: {
    flex: 1,
    minWidth: 0,
  },
  amount: {
    ...text.heading,
  },
  typeCaption: {
    ...text.caption,
    color: palette.brandText,
    marginTop: 2,
  },
  divider: {
    height: 1,
    backgroundColor: palette.line,
  },
  detailRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    gap: 12,
  },
  detailLabel: {
    ...text.caption,
  },
  detailValue: {
    ...text.body,
  },
  note: {
    ...text.bodyMuted,
  },
});
