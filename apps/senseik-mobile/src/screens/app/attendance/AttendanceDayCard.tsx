import React, { useState } from "react";
import { StyleSheet, Text, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { GlassCard, StatusPill } from "@/components/ui";
import { TR_MONTHS_SHORT, parseDateOnly } from "@/components/form/dateRange";
import { formatDurationMinutes } from "@/lib/formatters";
import { leaveTypeDisplayName } from "@/lib/leaveTypeNames";
import type { AttendanceDay } from "@/services/AttendanceService";
import { fontFor, palette, text } from "@/theme/theme";
import {
  ATTENDANCE_STATUS_PILLS,
  anomalyLabels,
  attendanceDayStatus,
  formatPunchLine,
  formatTimeOfDay,
} from "./attendanceStatus";

type Props = {
  day: AttendanceDay;
  /** "Fazla mesai: 1 sa 0 dk" / "Eksik mesai: ..." or `null` (`formatOvertimeUndertime`). */
  overtimeText: string | null;
};

/** Puantaj day card (spec §5): big day number, worked time, status pill, punches; tap for detail. */
export function AttendanceDayCard({ day, overtimeText }: Props) {
  const [expanded, setExpanded] = useState(false);
  const { year, month0, day: dayOfMonth } = parseDateOnly(day.date);
  const dayStatus = attendanceDayStatus(day);
  const status = ATTENDANCE_STATUS_PILLS[dayStatus];
  const leaveTypeText =
    dayStatus === "leave" && day.leaveTypeName ? leaveTypeDisplayName(day.leaveTypeName) : null;
  const worked = formatDurationMinutes(day.workedMinutes);
  const flags = anomalyLabels(day.anomalies);
  const hasPlan = Boolean(day.plannedStart && day.plannedEnd);

  return (
    <GlassCard
      style={styles.card}
      onPress={() => setExpanded((current) => !current)}
      accessibilityLabel={`${dayOfMonth} ${TR_MONTHS_SHORT[month0]} ${year}, ${status.label}, çalışma süresi ${worked}`}
    >
      <View style={styles.top}>
        <View style={styles.dateBlock}>
          <Text style={styles.dayNumber}>{dayOfMonth}</Text>
          <Text style={styles.monthYear}>
            {TR_MONTHS_SHORT[month0]} {year}
          </Text>
        </View>
        <View style={styles.divider} />
        <View style={styles.worked}>
          <Text style={styles.caption}>Çalışma süresi</Text>
          <Text style={styles.workedValue}>{worked}</Text>
        </View>
        <Ionicons
          name={expanded ? "chevron-down" : "chevron-forward"}
          size={20}
          color={palette.textFaint}
        />
      </View>

      <View style={styles.bottom}>
        <StatusPill label={status.label} tone={status.tone} />
        <Text style={styles.caption}>{formatPunchLine(day)}</Text>
      </View>
      {leaveTypeText ? <Text style={styles.caption}>{leaveTypeText}</Text> : null}
      {overtimeText ? <Text style={styles.caption}>{overtimeText}</Text> : null}

      {expanded ? (
        <View style={styles.details}>
          <Text style={styles.caption}>
            {hasPlan
              ? `Plan: ${formatTimeOfDay(day.plannedStart)} – ${formatTimeOfDay(day.plannedEnd)}`
              : "Planlı çalışma günü değil"}
          </Text>
          {flags.length > 0 ? <Text style={styles.caption}>{flags.join(" · ")}</Text> : null}
        </View>
      ) : null}
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 10,
    gap: 10,
  },
  top: {
    flexDirection: "row",
    alignItems: "center",
    gap: 14,
  },
  dateBlock: {
    width: 64,
  },
  dayNumber: {
    fontSize: 28,
    lineHeight: 32,
    color: palette.white,
    fontVariant: ["tabular-nums"],
    ...fontFor(800),
  },
  monthYear: {
    ...text.caption,
    color: palette.brandText,
  },
  divider: {
    width: 1,
    alignSelf: "stretch",
    backgroundColor: palette.line,
  },
  worked: {
    flex: 1,
  },
  workedValue: {
    ...text.heading,
    fontVariant: ["tabular-nums"],
  },
  caption: {
    ...text.caption,
  },
  bottom: {
    flexDirection: "row",
    alignItems: "center",
    gap: 10,
    flexWrap: "wrap",
  },
  details: {
    gap: 4,
    paddingTop: 8,
    borderTopWidth: 1,
    borderTopColor: palette.line,
  },
});
