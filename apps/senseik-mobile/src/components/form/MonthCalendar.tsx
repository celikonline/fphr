import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { toLocalDateOnly } from "@/lib/formatters";
import { fontFor, palette, radius, text, tones } from "@/theme/theme";
import {
  TR_WEEKDAYS_SHORT,
  formatDayMonthYear,
  formatMonthYear,
  isDayInRange,
  monthMatrix,
  parseDateOnly,
} from "./dateRange";

type Props = {
  year: number;
  month0: number;
  from?: string;
  to?: string;
  /** Days before this `DateOnly` are shown dimmed and cannot be tapped. */
  minDate?: string;
  onPrevMonth: () => void;
  onNextMonth: () => void;
  onDayPress: (day: string) => void;
};

/** Month grid for `DateRangeSheet`: Monday-first, range band between the two selected ends. */
export function MonthCalendar({
  year,
  month0,
  from,
  to,
  minDate,
  onPrevMonth,
  onNextMonth,
  onDayPress,
}: Props) {
  const today = toLocalDateOnly(new Date());
  const rows = monthMatrix(year, month0);

  return (
    <View>
      <View style={styles.header}>
        <TouchableOpacity
          style={styles.navButton}
          onPress={onPrevMonth}
          accessibilityRole="button"
          accessibilityLabel="Önceki ay"
        >
          <Ionicons name="chevron-back" size={20} color={palette.white} />
        </TouchableOpacity>
        <Text style={styles.monthLabel}>{formatMonthYear(year, month0)}</Text>
        <TouchableOpacity
          style={styles.navButton}
          onPress={onNextMonth}
          accessibilityRole="button"
          accessibilityLabel="Sonraki ay"
        >
          <Ionicons name="chevron-forward" size={20} color={palette.white} />
        </TouchableOpacity>
      </View>

      <View style={styles.row}>
        {TR_WEEKDAYS_SHORT.map((weekday) => (
          <Text key={weekday} style={styles.weekday}>
            {weekday}
          </Text>
        ))}
      </View>

      {rows.map((cells, rowIndex) => (
        <View key={rowIndex} style={styles.row}>
          {cells.map((day, columnIndex) =>
            day ? (
              <DayCell
                key={day}
                day={day}
                isEnd={day === from || day === to}
                inRange={isDayInRange(day, from, to)}
                isToday={day === today}
                disabled={Boolean(minDate && day < minDate)}
                onPress={() => onDayPress(day)}
              />
            ) : (
              <View key={`empty-${columnIndex}`} style={styles.cell} />
            )
          )}
        </View>
      ))}
    </View>
  );
}

function DayCell({
  day,
  isEnd,
  inRange,
  isToday,
  disabled,
  onPress,
}: {
  day: string;
  isEnd: boolean;
  inRange: boolean;
  isToday: boolean;
  disabled: boolean;
  onPress: () => void;
}) {
  return (
    <TouchableOpacity
      style={[styles.cell, inRange && styles.cellInRange]}
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={formatDayMonthYear(day)}
      accessibilityState={{ selected: isEnd || inRange, disabled }}
    >
      <View style={[styles.dayCircle, isToday && styles.today, isEnd && styles.dayEnd]}>
        <Text style={[styles.dayText, disabled && styles.dayDisabled, isEnd && styles.dayTextEnd]}>
          {parseDateOnly(day).day}
        </Text>
      </View>
    </TouchableOpacity>
  );
}

const CELL_SIZE = 44;
const CIRCLE_SIZE = 38;

const styles = StyleSheet.create({
  header: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    marginBottom: 6,
  },
  navButton: {
    width: CELL_SIZE,
    height: CELL_SIZE,
    borderRadius: radius.button,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  monthLabel: {
    ...text.heading,
  },
  row: {
    flexDirection: "row",
  },
  weekday: {
    ...text.caption,
    flex: 1,
    textAlign: "center",
    paddingVertical: 6,
    color: palette.textFaint,
  },
  cell: {
    flex: 1,
    height: CELL_SIZE,
    alignItems: "center",
    justifyContent: "center",
  },
  cellInRange: {
    backgroundColor: tones.info.bg,
  },
  dayCircle: {
    width: CIRCLE_SIZE,
    height: CIRCLE_SIZE,
    borderRadius: CIRCLE_SIZE / 2,
    alignItems: "center",
    justifyContent: "center",
  },
  today: {
    borderWidth: 1,
    borderColor: palette.brandText,
  },
  dayEnd: {
    backgroundColor: palette.brand,
  },
  dayText: {
    fontSize: 15,
    color: palette.white,
    fontVariant: ["tabular-nums"],
    ...fontFor(600),
  },
  dayTextEnd: {
    ...fontFor(800),
  },
  dayDisabled: {
    color: palette.textFaint,
  },
});
