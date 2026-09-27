import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  FlatList,
  RefreshControl,
  StyleSheet,
  Text,
  TouchableOpacity,
  View,
} from "react-native";
import { useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import { notificationsService, type UserNotification } from "@/services/NotificationsService";
import { formatRelativeTime } from "@/lib/formatters";
import {
  navigateToTarget,
  resolveNotificationNavigationTarget,
} from "@/lib/notificationNavigation";
import { unreadCountAfterMarkingAllRead, unreadCountAfterMarkingRead } from "./notificationsUnread";
import type { AppStackParamList } from "@/types/navigation";

const PAGE_SIZE = 20;

type NotificationsNavigationProp = NativeStackNavigationProp<AppStackParamList, "Notifications">;

function NotificationRow({
  notification,
  onPress,
}: {
  notification: UserNotification;
  onPress: () => void;
}) {
  const isUnread = !notification.readAt;
  return (
    <TouchableOpacity
      style={[styles.card, isUnread && styles.cardUnread]}
      onPress={onPress}
      accessibilityRole="button"
    >
      <Text style={[styles.cardTitle, isUnread && styles.cardTitleUnread]}>
        {notification.title}
      </Text>
      <Text style={styles.body}>{notification.body}</Text>
      <Text style={styles.time}>{formatRelativeTime(notification.createdAt)}</Text>
    </TouchableOpacity>
  );
}

export default function NotificationsScreen() {
  const navigation = useNavigation<NotificationsNavigationProp>();
  const [items, setItems] = useState<UserNotification[]>([]);
  const [page, setPage] = useState(1);
  const [totalCount, setTotalCount] = useState(0);
  const [unreadCount, setUnreadCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadFirstPage = useCallback(async () => {
    setError(null);
    try {
      const result = await notificationsService.listMyNotifications({
        page: 1,
        pageSize: PAGE_SIZE,
      });
      setItems(result.items);
      setPage(1);
      setTotalCount(result.totalCount);
      setUnreadCount(result.unreadCount);
    } catch {
      setError("Bildirimler yüklenemedi. Lütfen tekrar deneyin.");
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
      const result = await notificationsService.listMyNotifications({
        page: nextPage,
        pageSize: PAGE_SIZE,
      });
      setItems((current) => [...current, ...result.items]);
      setPage(nextPage);
      setTotalCount(result.totalCount);
    } catch {
      // A failed "load more" just stops paging silently - the user can pull-to-refresh to retry;
      // showing an error banner here would replace the (still valid) list already on screen.
    } finally {
      setLoadingMore(false);
    }
  }

  async function openNotification(notification: UserNotification) {
    const wasAlreadyRead = !!notification.readAt;
    if (!wasAlreadyRead) {
      setItems((current) =>
        current.map((item) =>
          item.id === notification.id ? { ...item, readAt: new Date().toISOString() } : item
        )
      );
      setUnreadCount((current) => unreadCountAfterMarkingRead(current, wasAlreadyRead));
      try {
        await notificationsService.markNotificationRead(notification.id);
      } catch {
        // Navigating away regardless - a failed "mark read" just leaves it unread until the next
        // reload; not worth blocking navigation or alerting over.
      }
    }

    const target = resolveNotificationNavigationTarget(notification.templateKey);
    if (target) {
      navigateToTarget(navigation, target);
    }
  }

  async function markAllRead() {
    const previousItems = items;
    const previousUnread = unreadCount;
    setItems((current) =>
      current.map((item) => ({ ...item, readAt: item.readAt ?? new Date().toISOString() }))
    );
    setUnreadCount(unreadCountAfterMarkingAllRead());
    try {
      await notificationsService.markAllNotificationsRead();
    } catch {
      setItems(previousItems);
      setUnreadCount(previousUnread);
      Alert.alert("Hata", "Bildirimler okundu olarak işaretlenemedi. Lütfen tekrar deneyin.");
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
    <View style={styles.container}>
      <View style={styles.header}>
        <Text style={styles.title}>Bildirimler</Text>
        <TouchableOpacity onPress={() => void markAllRead()} disabled={unreadCount === 0}>
          <Text style={[styles.markAllText, unreadCount === 0 && styles.markAllTextDisabled]}>
            Tümünü okundu işaretle
          </Text>
        </TouchableOpacity>
      </View>

      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      <FlatList
        data={items}
        keyExtractor={(item) => item.id}
        renderItem={({ item }) => (
          <NotificationRow notification={item} onPress={() => void openNotification(item)} />
        )}
        contentContainerStyle={styles.listContent}
        refreshControl={
          <RefreshControl refreshing={refreshing} onRefresh={() => void onRefresh()} />
        }
        onEndReachedThreshold={0.4}
        onEndReached={() => void loadMore()}
        ListEmptyComponent={<Text style={styles.emptyText}>Bildiriminiz yok.</Text>}
        ListFooterComponent={
          loadingMore ? <ActivityIndicator style={styles.footer} color={colors.primary} /> : null
        }
      />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.background,
    padding: spacing.xl,
  },
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: colors.background,
  },
  header: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    marginBottom: spacing.lg,
  },
  title: {
    ...typography.h3,
    color: colors.text,
  },
  markAllText: {
    ...typography.bodySm,
    color: colors.primary,
  },
  markAllTextDisabled: {
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
  listContent: {
    paddingBottom: spacing.xxxl,
  },
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm,
  },
  cardUnread: {
    backgroundColor: "#eaf2ff",
    borderColor: colors.primaryLight,
  },
  cardTitle: {
    ...typography.body,
    color: colors.text,
  },
  cardTitleUnread: {
    fontWeight: "700",
  },
  body: {
    ...typography.bodySm,
    color: colors.textSecondary,
    marginTop: spacing.xs,
  },
  time: {
    ...typography.bodyXs,
    color: colors.textTertiary,
    marginTop: spacing.xs,
  },
  emptyText: {
    ...typography.bodySm,
    color: colors.textTertiary,
  },
  footer: {
    marginVertical: spacing.md,
  },
});
