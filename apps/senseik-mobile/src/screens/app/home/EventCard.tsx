import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { GlassCard } from "@/components/ui";
import type { CompanyCalendarEntry } from "@/services/HomeFeedService";
import { fontFor, palette, text } from "@/theme/theme";
import { formatShortDate } from "./homeSections";

type Props = {
  event: CompanyCalendarEntry;
};

/**
 * "Yaklaşan etkinlikler" row (mockup `.event`): round icon + title, then date and description.
 * Not pressable - the mobile app has no event detail screen, so the mockup's chevron is omitted.
 */
export function EventCard({ event }: Props) {
  const meta = event.description
    ? `${formatShortDate(event.date)} · ${event.description}`
    : formatShortDate(event.date);

  return (
    <GlassCard style={styles.card}>
      <View style={styles.row}>
        <View
          style={styles.icon}
          accessibilityElementsHidden
          importantForAccessibility="no-hide-descendants"
        >
          <Text style={styles.iconText}>{event.type === "meeting" ? "👥" : "📋"}</Text>
        </View>
        <Text style={styles.title} numberOfLines={2}>
          {event.title}
        </Text>
      </View>
      <Text style={styles.meta} numberOfLines={2}>
        {meta}
      </Text>
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 12,
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
  },
  icon: {
    width: 46,
    height: 46,
    borderRadius: 23,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    backgroundColor: palette.glassInner,
    alignItems: "center",
    justifyContent: "center",
  },
  iconText: {
    fontSize: 18,
  },
  title: {
    flex: 1,
    fontSize: 15,
    lineHeight: 20,
    color: palette.white,
    ...fontFor(800),
  },
  meta: {
    ...text.caption,
    marginTop: 10,
    fontSize: 13,
    fontVariant: ["tabular-nums"],
  },
});
