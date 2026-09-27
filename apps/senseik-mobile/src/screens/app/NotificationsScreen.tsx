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
import { Ionicons } from "@expo/vector-icons";
import { useNavigation } from "@react-navigation/native";
import type { NativeStackNavigationProp } from "@react-navigation/native-stack";
import { EmptyState, ErrorBanner, GlassCard, Screen, ScreenHeader } from "@/components/ui";
import { notificationsService, type UserNotification } from "@/services/NotificationsService";
import { formatRelativeTime } from "@/lib/formatters";
import {
  navigateToTarget,
  resolveNotificationNavigationTarget,
} from "@/lib/notificationNavigation";
import { unreadCountAfterMarkingAllRead, unreadCountAfterMarkingRead } from "./notificationsUnread";
import { accents, fontFor, palette, radius, text, type AccentKey } from "@/theme/theme";
import type { AppStackParamList } from "@/types/navigation";

const PAGE_SIZE = 20;

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];
type NotificationsNavigationProp = NativeStackNavigationProp<AppStackParamList, "Notifications">;

/** N1: each `templateKey`'s dot-separated first segment ("payroll.payslip_ready" -> "payroll")
 * picks the category icon/accent - `UserNotification` carries no explicit `category` field. */
const CATEGORY_ICONS: Record<string, { icon: IoniconName; accent: AccentKey }> = {
  request: { icon: "document-text-outline", accent: "amber" },
  approval: { icon: "checkmark-done-outline", accent: "purple" },
  documents: { icon: "document-attach-outline", accent: "teal" },
  ohs: { icon: "alert-circle-outline", accent: "coral" },
  employee: { icon: "people-outline", accent: "blue" },
  announcement: { icon: "megaphone-outline", accent: "indigo" },
  payroll: { icon: "wallet-outline", accent: "coral" },
  leave: { icon: "sunny-outline", accent: "green" },
  onboarding: { icon: "flag-outline", accent: "pink" },
  account: { icon: "person-circle-outline", accent: "gray" },
};

const DEFAULT_CATEGORY_ICON: { icon: IoniconName; accent: AccentKey } = {
  icon: "notifications-outline",
  accent: "gray",
};

/** Maps a notification's `templateKey` to the category icon/accent shown on its row. */
export function notificationCategoryIcon(templateKey: string): {
  icon: IoniconName;
  accent: AccentKey;
} {
  const prefix = templateKey.split(".")[0];
  return CATEGORY_ICONS[prefix] ?? DEFAULT_CATEGORY_ICON;
}

function NotificationRow({
  notification,
  onPress,
}: {
  notification: UserNotification;
  onPress: () => void;
}) {
  const isUnread = !notification.readAt;
  const { icon, accent } = notificationCategoryIcon(notification.templateKey);
  const accentStyle = accents[accent];

  return (
    <GlassCard style={styles.card} onPress={onPress} accessibilityLabel={notification.title}>
      <View style={styles.row}>
        <View style={[styles.iconWrap, { backgroundColor: accentStyle.bg }]}>
          <Ionicons name={icon} size={20} color={accentStyle.fg} />
        </View>
        <View style={styles.content}>
          <View style={styles.titleRow}>
            {isUnread ? <View style={styles.dot} /> : null}
            <Text
              style={[styles.title, isUnread && styles.titleUnread]}
              numberOfLines={1}
            >
              {notification.title}
            </Text>
          </View>
          <Text style={styles.body} numberOfLines={2}>
            {notification.body}
          </Text>
          <Text style={styles.time}>{formatRelativeTime(notification.createdAt)}</Text>
        </View>
      </View>
    </GlassCard>
  );
}

function MarkAllReadPill({ disabled, onPress }: { disabled: boolean; onPress: () => void }) {
  return (
    <TouchableOpacity
      style={[styles.markAllPill, disabled && styles.markAllPillDisabled]}
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel="Tümünü okundu yap"
    >
      <Text style={[styles.markAllText, disabled && styles.markAllTextDisabled]}>
        Tümünü okundu yap
      </Text>
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

  const header = (
    <ScreenHeader
      title="Bildirimler"
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
          ListEmptyComponent={<EmptyState icon="notifications-outline" title="Bildiriminiz yok." />}
          ListFooterComponent={
            loadingMore ? (
              <ActivityIndicator style={styles.footer} color={palette.white} />
            ) : null
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
  markAllPill: {
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: radius.pill,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  markAllPillDisabled: {
    opacity: 0.5,
  },
  markAllText: {
    fontSize: 12,
    color: palette.brandText,
    ...fontFor(700),
  },
  markAllTextDisabled: {
    color: palette.textFaint,
  },
  listContent: {
    paddingHorizontal: 20,
    paddingBottom: 12,
  },
  card: {
    marginBottom: 10,
  },
  row: {
    flexDirection: "row",
    gap: 12,
  },
  iconWrap: {
    width: 40,
    height: 40,
    borderRadius: 14,
    alignItems: "center",
    justifyContent: "center",
  },
  content: {
    flex: 1,
    minWidth: 0,
  },
  titleRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
  },
  dot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    backgroundColor: palette.brand,
  },
  title: {
    ...text.body,
    flexShrink: 1,
  },
  titleUnread: {
    ...fontFor(800),
  },
  body: {
    ...text.bodyMuted,
    marginTop: 2,
  },
  time: {
    ...text.caption,
    marginTop: 4,
  },
  footer: {
    marginVertical: 12,
  },
});
