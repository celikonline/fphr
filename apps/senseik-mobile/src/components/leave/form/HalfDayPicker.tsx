import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { SegmentedControl } from "@/components/ui";
import type { LeaveHalfDayPeriod } from "@/services/LeaveService";
import { text } from "@/theme/theme";

const HALF_DAY_PERIOD_LABELS: Record<LeaveHalfDayPeriod, string> = {
  morning: "Sabah",
  afternoon: "Öğleden sonra",
};

const HALF_DAY_OPTIONS = (Object.keys(HALF_DAY_PERIOD_LABELS) as LeaveHalfDayPeriod[]).map(
  (period) => ({ value: period, label: HALF_DAY_PERIOD_LABELS[period] })
);

type Props = {
  value: LeaveHalfDayPeriod;
  onChange: (period: LeaveHalfDayPeriod) => void;
};

/** "Gün yarısı" (sabah / öğleden sonra) choice of a half-day leave request. */
export function HalfDayPicker({ value, onChange }: Props) {
  return (
    <View style={styles.group}>
      <Text style={styles.groupLabel}>Gün yarısı</Text>
      <SegmentedControl options={HALF_DAY_OPTIONS} value={value} onChange={onChange} />
    </View>
  );
}

const styles = StyleSheet.create({
  group: {
    gap: 6,
  },
  groupLabel: {
    ...text.caption,
  },
});
