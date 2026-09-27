import React, { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Alert, ScrollView, StyleSheet, Switch, Text, View } from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import {
  notificationsService,
  type NotificationChannel,
  type NotificationPreference,
} from "@/services/NotificationsService";
import {
  notificationCategoryLabel,
  notificationChannelLabel,
  notificationTemplateLabel,
} from "@/lib/notificationLabels";

function PreferenceRow({
  preference,
  onToggle,
  saving,
}: {
  preference: NotificationPreference;
  onToggle: (channel: NotificationChannel, enabled: boolean) => void;
  saving: boolean;
}) {
  return (
    <View style={styles.card}>
      <Text style={styles.cardTitle}>{notificationTemplateLabel(preference.templateKey)}</Text>
      <Text style={styles.cardCategory}>{notificationCategoryLabel(preference.category)}</Text>
      {preference.channels.map((channel) => (
        <View key={channel.channel} style={styles.channelRow}>
          <Text style={styles.channelLabel}>{notificationChannelLabel(channel.channel)}</Text>
          <Switch
            value={channel.enabled}
            disabled={saving || channel.locked || preference.isMandatory}
            onValueChange={(value) => onToggle(channel.channel, value)}
          />
        </View>
      ))}
      {preference.isMandatory && <Text style={styles.mandatoryText}>Kapatılamaz</Text>}
    </View>
  );
}

export default function NotificationPreferencesScreen() {
  const [preferences, setPreferences] = useState<NotificationPreference[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const result = await notificationsService.getPreferences();
      setPreferences(result);
    } catch {
      setError("Bildirim tercihleri yüklenemedi. Lütfen tekrar deneyin.");
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void load().finally(() => setLoading(false));
  }, [load]);

  async function toggleChannel(
    templateKey: string,
    channel: NotificationChannel,
    enabled: boolean
  ) {
    const previous = preferences;
    setPreferences((current) =>
      current.map((preference) =>
        preference.templateKey === templateKey
          ? {
              ...preference,
              channels: preference.channels.map((c) =>
                c.channel === channel ? { ...c, enabled } : c
              ),
            }
          : preference
      )
    );
    setSaving(true);
    try {
      await notificationsService.updatePreferences([{ templateKey, channel, enabled }]);
    } catch {
      setPreferences(previous);
      Alert.alert("Hata", "Bildirim tercihi kaydedilemedi. Lütfen tekrar deneyin.");
    } finally {
      setSaving(false);
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
    <ScrollView style={styles.container} contentContainerStyle={styles.content}>
      <Text style={styles.title}>Bildirim tercihleri</Text>
      <Text style={styles.subtitle}>
        Hangi bildirimleri e-posta ile ve uygulama içinde alacağınızı seçin.
      </Text>

      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      {!error && preferences.length === 0 ? (
        <Text style={styles.emptyText}>Ayarlanabilir bildirim yok.</Text>
      ) : (
        preferences.map((preference) => (
          <PreferenceRow
            key={preference.templateKey}
            preference={preference}
            saving={saving}
            onToggle={(channel, enabled) =>
              void toggleChannel(preference.templateKey, channel, enabled)
            }
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
  },
  subtitle: {
    ...typography.bodySm,
    color: colors.textSecondary,
    marginTop: spacing.xs,
    marginBottom: spacing.lg,
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
  emptyText: {
    ...typography.bodySm,
    color: colors.textTertiary,
  },
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm,
  },
  cardTitle: {
    ...typography.body,
    color: colors.text,
    fontWeight: "600",
  },
  cardCategory: {
    ...typography.bodyXs,
    color: colors.textTertiary,
    marginTop: 2,
    marginBottom: spacing.sm,
  },
  channelRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    paddingVertical: spacing.xs,
  },
  channelLabel: {
    ...typography.bodySm,
    color: colors.text,
  },
  mandatoryText: {
    ...typography.bodyXs,
    color: colors.textTertiary,
    marginTop: spacing.xs,
  },
});
