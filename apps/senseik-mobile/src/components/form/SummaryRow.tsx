import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { palette, radius, text, tones } from "@/theme/theme";

type Props = {
  label: string;
  value: string;
  /** Renders the value in the danger tone (e.g. a zero-day leave preview). */
  invalid?: boolean;
};

/** "Süre: 10 gün"-style read-only line under a request form's date/time fields. */
export function SummaryRow({ label, value, invalid = false }: Props) {
  return (
    <View style={styles.row} accessibilityLabel={`${label}: ${value}`}>
      <Text style={styles.label}>{label}</Text>
      <Text style={[styles.value, invalid && styles.valueInvalid]}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 12,
    paddingHorizontal: 14,
    paddingVertical: 12,
    borderRadius: radius.button,
    backgroundColor: palette.glassInner,
  },
  label: {
    ...text.bodyMuted,
  },
  value: {
    ...text.heading,
    fontVariant: ["tabular-nums"],
  },
  valueInvalid: {
    color: tones.danger.fg,
  },
});
