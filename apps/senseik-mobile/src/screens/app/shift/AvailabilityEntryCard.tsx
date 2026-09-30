import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { useTranslation } from "react-i18next";
import { GlassCard } from "@/components/ui";
import type { EmployeeAvailability } from "@/services/ShiftService";
import { fontFor, palette, text } from "@/theme/theme";
import { availabilityKindLabel } from "./AvailabilityForm";
import { formatDayLabel } from "./shiftWeek";

type Props = {
  entry: EmployeeAvailability;
  timeRange: string;
  onCancel: () => void;
};

/** One active "Müsaitlik kaydım" row with its "İptal et" action. */
export function AvailabilityEntryCard({ entry, timeRange, onCancel }: Props) {
  const { t } = useTranslation("shift");
  return (
    <GlassCard style={styles.card}>
      <View style={styles.header}>
        <Text style={styles.day}>{formatDayLabel(entry.date)}</Text>
        <TouchableOpacity
          onPress={onCancel}
          accessibilityRole="button"
          accessibilityLabel={t("availability.cancel")}
          // Visual height ~16px; hitSlop brings the tappable area to >=44.
          hitSlop={{ top: 14, bottom: 14, left: 10, right: 10 }}
        >
          <Text style={styles.cancel}>{t("availability.cancel")}</Text>
        </TouchableOpacity>
      </View>
      <Text style={styles.kind}>{availabilityKindLabel(entry.kind)}</Text>
      <Text style={styles.caption}>{timeRange}</Text>
      {entry.note ? <Text style={styles.caption}>{entry.note}</Text> : null}
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 10,
    gap: 4,
  },
  header: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  day: {
    ...text.bodyMuted,
  },
  cancel: {
    ...text.caption,
    color: palette.coral,
    ...fontFor(700),
  },
  kind: {
    ...text.heading,
  },
  caption: {
    ...text.caption,
  },
});
