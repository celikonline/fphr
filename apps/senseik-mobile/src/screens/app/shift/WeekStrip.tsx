import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { formatDayMonth, parseDateOnly } from "@/components/form/dateRange";
import { fontFor, palette, radius, text } from "@/theme/theme";
import { weekdayShort } from "./shiftWeek";

type Props = {
  days: string[];
  today: string;
  /** Day the lists are narrowed to, or `null` for the whole week. */
  selected: string | null;
  /** Days with one of the caller's own shifts (shown with a dot). */
  markedDays: Set<string>;
  onSelect: (day: string) => void;
};

/** Vardiyam's week strip: one glass cell per day; tapping a day narrows the lists to it. */
export function WeekStrip({ days, today, selected, markedDays, onSelect }: Props) {
  return (
    <View style={styles.row}>
      {days.map((day) => {
        const isSelected = day === selected;
        return (
          <TouchableOpacity
            key={day}
            style={[styles.cell, day === today && styles.today, isSelected && styles.selected]}
            onPress={() => onSelect(day)}
            accessibilityRole="button"
            accessibilityLabel={`${weekdayShort(day)} ${formatDayMonth(day)}`}
            accessibilityState={{ selected: isSelected }}
          >
            <Text style={[styles.weekday, isSelected && styles.textSelected]}>
              {weekdayShort(day)}
            </Text>
            <Text style={[styles.dayNumber, isSelected && styles.textSelected]}>
              {parseDateOnly(day).day}
            </Text>
            <View style={[styles.dot, markedDays.has(day) && styles.dotVisible]} />
          </TouchableOpacity>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    gap: 6,
    paddingHorizontal: 20,
  },
  cell: {
    flex: 1,
    alignItems: "center",
    gap: 2,
    minHeight: 64,
    paddingVertical: 8,
    borderRadius: radius.button,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  today: {
    borderColor: palette.brandText,
  },
  selected: {
    backgroundColor: palette.brand,
    borderColor: palette.brand,
  },
  weekday: {
    ...text.caption,
    fontSize: 11,
  },
  dayNumber: {
    fontSize: 17,
    color: palette.white,
    fontVariant: ["tabular-nums"],
    ...fontFor(800),
  },
  textSelected: {
    color: palette.white,
  },
  dot: {
    width: 5,
    height: 5,
    borderRadius: 3,
  },
  dotVisible: {
    backgroundColor: palette.brandText,
  },
});
