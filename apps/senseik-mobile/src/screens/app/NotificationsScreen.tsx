import React, { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Alert, FlatList, RefreshControl, StyleSheet, View } from "react-native";
import { useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { useTranslation } from "react-i18next";
import i18n from "@/lib/i18n";
import { EmptyState, ErrorBanner, Screen, ScreenHeader } from "@/components/ui";
import { notificationsService, type UserNotification } from "@/services/NotificationsService";
import {
  navigateToTarget,
  resolveNotificationNavigationTarget,
  resolveRequestDetailTarget,
} from "@/lib/notificationNavigation";
import { unreadCountAfterMarkingAllRead, unreadCountAfterMarkingRead } from "./notificationsUnread";
import { palette } from "@/theme/theme";
import type { AppStackParamList } from "@/types/navigation";
import { MarkAllReadPill } from "./notifications/MarkAllReadPill";
import { NotificationRow } from "./notifications/NotificationRow";

/** Re-exported so existing tests/imports keep working from this screen's path. */
export { notificationCategoryIcon } from "./notifications/notificationCategoryIcon";

const PAGE_SIZE = 20;

type NotificationsNavigationProp = NativeStackNavigationProp<AppStackParamList, "Notifications">;

export default function NotificationsScreen() {
  const { t } = useTranslation("notifications");
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
      setError(i18n.t("notifications:loadError"));
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

    // FG-009: `request.comment_added` has no static target (its screen needs an id) - resolved from
    // `actionUrl` instead, see `resolveRequestDetailTarget`'s doc comment.
    const requestDetail = resolveRequestDetailTarget(notification.actionUrl);
    if (requestDetail) {
      navigation.navigate("RequestDetail", requestDetail);
      return;
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
      Alert.alert(t("common:alerts.error"), t("markAllReadError"));
    }
  }

  const header = (
    <ScreenHeader
      title={t("title")}
      right={<MarkAllReadPill disabled={unreadCount === 0} onPress={() => void markAllRead()} />}
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

  return (
    <Screen scroll={false} header={header}>
      <View style={styles.flex}>
        {error ? <ErrorBanner message={error} onRetry={() => void onRefresh()} /> : null}

        <FlatList
          data={items}
          keyExtractor={(item) => item.id}
          renderItem={({ item }) => (
            <NotificationRow notification={item} onPress={() => void openNotification(item)} />
          )}
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
          ListEmptyComponent={<EmptyState icon="notifications-outline" title={t("empty")} />}
          ListFooterComponent={
            loadingMore ? <ActivityIndicator style={styles.footer} color={palette.white} /> : null
          }
        />
      </View>
    </Screen>
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
});
