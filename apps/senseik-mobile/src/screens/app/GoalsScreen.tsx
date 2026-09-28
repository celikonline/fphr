import React, { useCallback, useEffect, useState } from "react";
import { ActivityIndicator, Alert, StyleSheet, View } from "react-native";
import { useTranslation } from "react-i18next";
import i18n from "@/lib/i18n";
import { EmptyState, ErrorBanner, Screen, ScreenHeader } from "@/components/ui";
import { goalsService, type Goal } from "@/services/GoalsService";
import { palette } from "@/theme/theme";
import { GoalCard } from "./goals/GoalCard";
import { GOAL_EDITABLE_STATUSES } from "./goals/goalStatus";

export default function GoalsScreen() {
  const { t } = useTranslation("goals");
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
      setError(i18n.t("goals:loadError"));
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
      Alert.alert(t("invalidTitle"), t("invalidProgress"));
      return;
    }

    setSavingId(goal.id);
    try {
      await goalsService.updateMyGoalProgress(goal.id, parsed);
      await loadGoals();
    } catch {
      Alert.alert(t("common:alerts.error"), t("saveError"));
    } finally {
      setSavingId(null);
    }
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

  return (
    <Screen header={header} refreshing={refreshing} onRefresh={() => void onRefresh()}>
      {error ? <ErrorBanner message={error} onRetry={() => void onRefresh()} /> : null}

      {!error && goals.length === 0 ? (
        <EmptyState icon="flag-outline" title={t("empty")} />
      ) : (
        goals.map((goal) => (
          <GoalCard
            key={goal.id}
            goal={goal}
            editable={GOAL_EDITABLE_STATUSES.has(goal.status)}
            draft={progressDrafts[goal.id] ?? String(goal.progress)}
            saving={savingId === goal.id}
            onDraftChange={(value) =>
              setProgressDrafts((current) => ({ ...current, [goal.id]: value }))
            }
            onSave={() => void saveProgress(goal)}
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
});
