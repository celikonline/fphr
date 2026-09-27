import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { GlassCard, StatusPill } from "@/components/ui";
import { fontFor, palette, text } from "@/theme/theme";

type Props = {
  title: string;
  timeRange: string;
  isNight: boolean;
  note?: string | null;
};

/** One shift row (own or team): day label, "09:00 – 18:00", "Gece" pill, optional note. */
export function ShiftCard({ title, timeRange, isNight, note }: Props) {
  return (
    <GlassCard style={styles.card}>
      <View style={styles.header}>
        <Text style={styles.title} numberOfLines={1}>
          {title}
        </Text>
        {isNight ? <StatusPill label="Gece" tone="info" /> : null}
      </View>
      <Text style={styles.time}>{timeRange}</Text>
      {note ? <Text style={styles.note}>{note}</Text> : null}
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
    gap: 8,
  },
  title: {
    ...text.bodyMuted,
    flex: 1,
  },
  time: {
    fontSize: 22,
    lineHeight: 28,
    color: palette.white,
    fontVariant: ["tabular-nums"],
    ...fontFor(800),
  },
  note: {
    ...text.caption,
  },
});
