import React, { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Alert, StyleSheet, Switch, Text, View } from "react-native";
import { EmptyState, ErrorBanner, GlassCard, Screen, ScreenHeader } from "@/components/ui";
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
import { palette, text } from "@/theme/theme";

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
    <GlassCard style={styles.card}>
      <Text style={styles.cardTitle}>{notificationTemplateLabel(preference.templateKey)}</Text>
      <Text style={styles.cardCategory}>{notificationCategoryLabel(preference.category)}</Text>
      {preference.channels.map((channel) => (
        <View key={channel.channel} style={styles.channelRow}>
          <Text style={styles.channelLabel}>{notificationChannelLabel(channel.channel)}</Text>
          <Switch
            value={channel.enabled}
            disabled={saving || channel.locked || preference.isMandatory}
            trackColor={{ false: palette.glassBorder, true: palette.brand }}
            thumbColor={palette.white}
            onValueChange={(value) => onToggle(channel.channel, value)}
          />
        </View>
      ))}
      {preference.isMandatory && <Text style={styles.mandatoryText}>Kapatılamaz</Text>}
    </GlassCard>
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

  const header = (
    <ScreenHeader
      title="Bildirim tercihleri"
      subtitle="Hangi bildirimleri e-posta ile ve uygulama içinde alacağınızı seçin."
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
    <Screen header={header}>
      {error ? <ErrorBanner message={error} onRetry={() => void load()} /> : null}

      {!error && preferences.length === 0 ? (
        <EmptyState icon="notifications-outline" title="Ayarlanabilir bildirim yok." />
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
    </Screen>
  );
}

const styles = StyleSheet.create({
  centered: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
  },
  card: {
    marginHorizontal: 20,
    marginBottom: 10,
  },
  cardTitle: {
    ...text.heading,
  },
  cardCategory: {
    ...text.caption,
    marginTop: 2,
    marginBottom: 10,
  },
  channelRow: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    paddingVertical: 6,
  },
  channelLabel: {
    ...text.bodyMuted,
  },
  mandatoryText: {
    ...text.caption,
    marginTop: 6,
  },
});
