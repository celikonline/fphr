import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { SegmentedControl } from "@/components/ui";
import type { LeaveHalfDayPeriod } from "@/services/LeaveService";
import { text } from "@/theme/theme";

const HALF_DAY_PERIODS: readonly LeaveHalfDayPeriod[] = ["morning", "afternoon"];

type Props = {
  value: LeaveHalfDayPeriod;
  onChange: (period: LeaveHalfDayPeriod) => void;
};

/** "Gün yarısı" (sabah / öğleden sonra) choice of a half-day leave request. */
export function HalfDayPicker({ value, onChange }: Props) {
  const { t } = useTranslation("leave");
  const options = HALF_DAY_PERIODS.map((period) => ({
    value: period,
    label: t(`form.halfDay.${period}`),
  }));
  return (
    <View style={styles.group}>
      <Text style={styles.groupLabel}>{t("form.halfDay.label")}</Text>
      <SegmentedControl options={options} value={value} onChange={onChange} />
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
