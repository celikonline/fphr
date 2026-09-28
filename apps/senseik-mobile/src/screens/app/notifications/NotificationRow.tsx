import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { GlassCard } from "@/components/ui";
import { formatRelativeTime } from "@/lib/formatters";
import type { UserNotification } from "@/services/NotificationsService";
import { accents, fontFor, palette, text } from "@/theme/theme";
import { notificationCategoryIcon } from "./notificationCategoryIcon";

type Props = {
  notification: UserNotification;
  onPress: () => void;
};

/** One notification row: category icon, unread dot + 800 title, body, relative time. */
export function NotificationRow({ notification, onPress }: Props) {
  useTranslation();
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
            <Text style={[styles.title, isUnread && styles.titleUnread]} numberOfLines={1}>
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

const styles = StyleSheet.create({
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
});
