import React from "react";
import { Platform, StyleSheet, Text, TouchableOpacity } from "react-native";
import { DateTimePickerAndroid } from "@react-native-community/datetimepicker";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import { dateOnlyToDate, formatDateWithWeekday, toLocalDateOnly } from "@/lib/formatters";

type Props = {
  /** `DateOnly` ("yyyy-MM-dd"). */
  value: string;
  onChange: (value: string) => void;
  /** `DateOnly`; the picker won't let the user go earlier than this (e.g. an end date's start date). */
  minimumDateValue?: string;
  disabled?: boolean;
};

/**
 * Tap-to-open native date picker, replacing a free-text "yyyy-aa-gg" box (D-mobil-005). Android
 * only for now: this app's dev target is the Android emulator (see README's "Android development
 * build" - there is no documented/tested iOS dev flow yet), and `DateTimePickerAndroid`'s imperative
 * API is the library's own recommended approach on Android (its README: "we recommend using the
 * imperative API on Android"). On any other platform the field shows the value but tapping is a
 * no-op until an iOS picker is designed.
 */
export function DateField({ value, onChange, minimumDateValue, disabled }: Props) {
  function open() {
    if (Platform.OS !== "android" || disabled) {
      return;
    }
    DateTimePickerAndroid.open({
      value: dateOnlyToDate(value),
      mode: "date",
      minimumDate: minimumDateValue ? dateOnlyToDate(minimumDateValue) : undefined,
      onValueChange: (_event, selected) => {
        if (selected) {
          onChange(toLocalDateOnly(selected));
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
      <Text style={styles.fieldText}>{formatDateWithWeekday(value)}</Text>
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
