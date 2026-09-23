import React, { useCallback, useEffect, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  RefreshControl,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from "react-native";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import { goalsService, type Goal, type GoalStatus } from "@/services/GoalsService";

function formatDateOnly(value: string): string {
  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleDateString("tr-TR", { day: "2-digit", month: "short", year: "numeric" });
}

const STATUS_LABELS: Record<GoalStatus, string> = {
  open: "Açık",
  inProgress: "Devam ediyor",
  completed: "Tamamlandı",
  cancelled: "İptal edildi",
  archived: "Arşivlendi",
};

const STATUS_COLORS: Record<GoalStatus, string> = {
  open: colors.textTertiary,
  inProgress: colors.warning,
  completed: colors.success,
  cancelled: colors.error,
  archived: colors.textTertiary,
};

/** Goals whose progress the caller can still edit self-service (mirrors `Goal.UpdateProgress` domain guard intent). */
const EDITABLE_STATUSES: ReadonlySet<GoalStatus> = new Set(["open", "inProgress"]);

export default function GoalsScreen() {
  const [goals, setGoals] = useState<Goal[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [progressDrafts, setProgressDrafts] = useState<Record<string, string>>({});
  const [savingId, setSavingId] = useState<string | null>(null);

  const loadGoals = useCallback(async () => {
    setError(null);
    try {
      const result = await goalsService.getMyGoals();
      setGoals(result);
      setProgressDrafts((current) => {
        const next = { ...current };
        for (const goal of result) {
          if (next[goal.id] === undefined) {
            next[goal.id] = String(goal.progress);
          }
        }
        return next;
      });
    } catch {
      setError("Hedefler yüklenemedi. Lütfen tekrar deneyin.");
    }
  }, []);

  useEffect(() => {
    setLoading(true);
    void loadGoals().finally(() => setLoading(false));
  }, [loadGoals]);

  async function onRefresh() {
    setRefreshing(true);
    await loadGoals();
    setRefreshing(false);
  }

  async function saveProgress(goal: Goal) {
    const draft = progressDrafts[goal.id] ?? String(goal.progress);
    const parsed = Number(draft);
    if (!Number.isInteger(parsed) || parsed < 0 || parsed > 100) {
      Alert.alert("Geçersiz değer", "İlerleme 0 ile 100 arasında bir tam sayı olmalı.");
      return;
    }

    setSavingId(goal.id);
    try {
      await goalsService.updateMyGoalProgress(goal.id, parsed);
      await loadGoals();
    } catch {
      Alert.alert("Hata", "İlerleme güncellenemedi. Lütfen tekrar deneyin.");
    } finally {
      setSavingId(null);
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
    <ScrollView
      style={styles.container}
      contentContainerStyle={styles.content}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={() => void onRefresh()} />}
    >
      <Text style={styles.title}>Hedeflerim</Text>

      {error && (
        <View style={styles.errorBanner}>
          <Text style={styles.errorText}>{error}</Text>
        </View>
      )}

      {goals.length === 0 ? (
        <Text style={styles.emptyText}>Henüz bir hedefiniz yok.</Text>
      ) : (
        goals.map((goal) => {
          const editable = EDITABLE_STATUSES.has(goal.status);
          const draft = progressDrafts[goal.id] ?? String(goal.progress);
          const saving = savingId === goal.id;

          return (
            <View key={goal.id} style={styles.card}>
              <View style={styles.goalHeader}>
                <Text style={styles.cardLabel}>{goal.title}</Text>
                <View style={[styles.statusBadge, { backgroundColor: STATUS_COLORS[goal.status] }]}>
                  <Text style={styles.statusBadgeText}>{STATUS_LABELS[goal.status]}</Text>
                </View>
              </View>

              {goal.description && <Text style={styles.cardSubtext}>{goal.description}</Text>}
              <Text style={styles.cardSubtext}>Hedef tarih: {formatDateOnly(goal.targetDate)}</Text>

              <View style={styles.progressRow}>
                <View style={styles.progressTrack}>
                  <View
                    style={[
                      styles.progressFill,
                      { width: `${Math.min(100, Math.max(0, goal.progress))}%` },
                    ]}
                  />
                </View>
                <Text style={styles.progressText}>%{goal.progress}</Text>
              </View>

              {editable && (
                <View style={styles.progressEditRow}>
                  <TextInput
                    style={styles.progressInput}
                    value={draft}
                    onChangeText={(text) =>
                      setProgressDrafts((current) => ({ ...current, [goal.id]: text }))
                    }
                    keyboardType="number-pad"
                    maxLength={3}
                  />
                  <TouchableOpacity
                    style={[styles.saveButton, saving && styles.saveButtonDisabled]}
                    onPress={() => void saveProgress(goal)}
                    disabled={saving}
                  >
                    {saving ? (
                      <ActivityIndicator color={colors.white} />
                    ) : (
                      <Text style={styles.saveButtonText}>Kaydet</Text>
                    )}
                  </TouchableOpacity>
                </View>
              )}
            </View>
          );
        })
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
  card: {
    backgroundColor: colors.white,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 12,
    padding: spacing.lg,
    marginBottom: spacing.sm,
  },
  cardLabel: {
    ...typography.bodySm,
    color: colors.textSecondary,
    flexShrink: 1,
    marginRight: spacing.sm,
  },
  cardSubtext: {
    ...typography.bodyXs,
    color: colors.textTertiary,
    marginTop: spacing.xs,
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
  goalHeader: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
  },
  statusBadge: {
    borderRadius: 12,
    paddingHorizontal: spacing.sm,
    paddingVertical: 2,
  },
  statusBadgeText: {
    ...typography.caption,
    color: colors.white,
  },
  progressRow: {
    flexDirection: "row",
    alignItems: "center",
    marginTop: spacing.md,
  },
  progressTrack: {
    flex: 1,
    height: 8,
    borderRadius: 4,
    backgroundColor: colors.gray50,
    overflow: "hidden",
    marginRight: spacing.sm,
  },
  progressFill: {
    height: "100%",
    backgroundColor: colors.primary,
    borderRadius: 4,
  },
  progressText: {
    ...typography.bodyXs,
    color: colors.textSecondary,
  },
  progressEditRow: {
    flexDirection: "row",
    alignItems: "center",
    marginTop: spacing.md,
  },
  progressInput: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 8,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    ...typography.body,
    color: colors.text,
    backgroundColor: colors.white,
    width: 72,
    marginRight: spacing.sm,
  },
  saveButton: {
    backgroundColor: colors.primary,
    borderRadius: 8,
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.lg,
    alignItems: "center",
  },
  saveButtonDisabled: {
    opacity: 0.6,
  },
  saveButtonText: {
    ...typography.button,
    color: colors.white,
  },
});
