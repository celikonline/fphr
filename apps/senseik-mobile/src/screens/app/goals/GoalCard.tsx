import React from "react";
import { StyleSheet, Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import { GlassCard, PrimaryButton, StatusPill } from "@/components/ui";
import type { Goal } from "@/services/GoalsService";
import { fontFor, palette, text } from "@/theme/theme";
import { formatDate } from "@/lib/formatters";
import { goalStatusLabel, goalStatusTone } from "./goalStatus";

function formatDateOnly(value: string): string {
  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  return formatDate(date, { day: "2-digit", month: "short", year: "numeric" });
}

type Props = {
  goal: Goal;
  editable: boolean;
  draft: string;
  saving: boolean;
  onDraftChange: (value: string) => void;
  onSave: () => void;
};

/** One goal's card: title/status, progress bar, and (when editable) a progress edit row. */
export function GoalCard({ goal, editable, draft, saving, onDraftChange, onSave }: Props) {
  const { t } = useTranslation("goals");
  const progress = Math.min(100, Math.max(0, goal.progress));

  return (
    <GlassCard style={styles.card}>
      <View style={styles.header}>
        <Text style={styles.title}>{goal.title}</Text>
        <StatusPill label={goalStatusLabel(goal.status)} tone={goalStatusTone(goal.status)} />
      </View>

      {goal.description ? <Text style={styles.subtext}>{goal.description}</Text> : null}
      <Text style={styles.subtext}>
        {t("targetDate", { date: formatDateOnly(goal.targetDate) })}
      </Text>

      <View style={styles.progressRow}>
        <View style={styles.progressTrack}>
          <View style={[styles.progressFill, { width: `${progress}%` }]} />
        </View>
        <Text style={styles.progressText}>{t("percent", { value: goal.progress })}</Text>
      </View>

      {editable ? (
        <View style={styles.editRow}>
          <TextInput
            style={styles.progressInput}
            value={draft}
            onChangeText={onDraftChange}
            keyboardType="number-pad"
            maxLength={3}
            accessibilityLabel={t("progressA11y", { title: goal.title })}
          />
          <PrimaryButton label={t("common:actions.save")} onPress={onSave} loading={saving} />
        </View>
      ) : null}
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 10,
  },
  header: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: 12,
  },
  title: {
    ...text.heading,
    flex: 1,
  },
  subtext: {
    ...text.bodyMuted,
    marginTop: 6,
  },
  progressRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    marginTop: 14,
  },
  progressTrack: {
    flex: 1,
    height: 8,
    borderRadius: 4,
    backgroundColor: palette.line,
    overflow: "hidden",
  },
  progressFill: {
    height: "100%",
    borderRadius: 4,
    backgroundColor: palette.brand,
  },
  progressText: {
    ...text.caption,
    fontVariant: ["tabular-nums"],
  },
  editRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    marginTop: 14,
  },
  progressInput: {
    width: 72,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    borderRadius: 14,
    paddingHorizontal: 12,
    paddingVertical: 10,
    fontSize: 15,
    color: palette.white,
    ...fontFor(600),
  },
});
