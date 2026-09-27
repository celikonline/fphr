import React from "react";
import { Platform, StyleSheet, Text, TouchableOpacity } from "react-native";
import { DateTimePickerAndroid } from "@react-native-community/datetimepicker";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import { dateToTimeOnly, timeOnlyToDate } from "@/lib/formatters";

type Props = {
  /** `TimeOnly`-shaped "HH:mm". */
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
};

/** Tap-to-open native time picker for hourly leave start/end times; see `DateField` for the Android-only rationale. */
export function TimeField({ value, onChange, disabled }: Props) {
  function open() {
    if (Platform.OS !== "android" || disabled) {
      return;
    }
    DateTimePickerAndroid.open({
      value: timeOnlyToDate(value),
      mode: "time",
      is24Hour: true,
      onValueChange: (_event, selected) => {
        if (selected) {
          onChange(dateToTimeOnly(selected));
        }
      },
    });
  }

  return (
    <TouchableOpacity
      style={[styles.field, disabled && styles.fieldDisabled]}
      onPress={open}
      disabled={disabled}
    >
      <Text style={styles.fieldText}>{value}</Text>
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  field: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 8,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    backgroundColor: colors.white,
  },
  fieldDisabled: {
    opacity: 0.6,
  },
  fieldText: {
    ...typography.body,
    color: colors.text,
  },
});
