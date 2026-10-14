import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { formatDayMonth, monthMatrix, parseDateOnly, weekdaysShort } from "@/components/form/dateRange";
import { fontFor, palette, radius, text } from "@/theme/theme";
import { weekdayShort } from "./shiftWeek";

type Props = {
  year: number;
  month0: number;
  today: string;
  selected: string | null;
  markedDays: Set<string>;
  shiftLabel: string;
  onSelect: (day: string) => void;
};

/** Monday-first monthly calendar. Days with a published personal shift have a visible marker. */
export function MonthCalendar({
  year,
  month0,
  today,
  selected,
  markedDays,
  shiftLabel,
  onSelect,
}: Props) {
  const headers = weekdaysShort();
  const weeks = monthMatrix(year, month0);

  return (
    <View style={styles.container}>
      <View style={styles.row}>
        {headers.map((day) => (
          <Text key={day} style={styles.weekday} accessibilityRole="text">
            {day}
          </Text>
        ))}
      </View>
      {weeks.map((week, rowIndex) => (
        <View key={rowIndex} style={styles.row}>
          {week.map((day, columnIndex) => {
            if (!day) return <View key={`empty-${rowIndex}-${columnIndex}`} style={styles.cell} />;
            const isSelected = day === selected;
            const hasShift = markedDays.has(day);
            return (
              <TouchableOpacity
                key={day}
                style={[
                  styles.cell,
                  day === today && styles.today,
                  isSelected && styles.selected,
                ]}
                onPress={() => onSelect(day)}
                accessibilityRole="button"
                accessibilityLabel={`${weekdayShort(day)} ${formatDayMonth(day)}${hasShift ? `, ${shiftLabel}` : ""}`}
                accessibilityState={{ selected: isSelected }}
              >
                <Text style={[styles.dayNumber, isSelected && styles.textSelected]}>
                  {parseDateOnly(day).day}
                </Text>
                <View
                  style={[
                    styles.dot,
                    hasShift && styles.dotVisible,
                    isSelected && hasShift && styles.dotSelected,
                  ]}
                />
              </TouchableOpacity>
            );
          })}
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: 5,
    paddingHorizontal: 20,
  },
  row: {
    flexDirection: "row",
    gap: 6,
  },
  weekday: {
    ...text.caption,
    flex: 1,
    textAlign: "center",
    paddingVertical: 4,
    fontSize: 11,
  },
  cell: {
    flex: 1,
    minHeight: 43,
    alignItems: "center",
    justifyContent: "center",
    gap: 3,
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
  dayNumber: {
    fontSize: 15,
    color: palette.white,
    fontVariant: ["tabular-nums"],
    ...fontFor(700),
  },
  textSelected: {
    color: palette.white,
  },
  dot: {
    width: 4,
    height: 4,
    borderRadius: 2,
  },
  dotVisible: {
    backgroundColor: palette.brandText,
  },
  dotSelected: {
    backgroundColor: palette.white,
  },
});
