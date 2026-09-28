import React from "react";
import { Platform, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { DateTimePickerAndroid } from "@react-native-community/datetimepicker";
import { dateOnlyToDate, formatDateWithWeekday, toLocalDateOnly } from "@/lib/formatters";
import { palette } from "@/theme/theme";
import { pickerFieldStyles as styles } from "./fieldStyles";

type Props = {
  /** `DateOnly` ("yyyy-MM-dd"). */
  value: string;
  onChange: (value: string) => void;
  /** `DateOnly`; the picker won't let the user go earlier than this (e.g. an end date's start date). */
  minimumDateValue?: string;
  disabled?: boolean;
  /** Caption shown above the glass box (and used as its accessible name). */
  label?: string;
  required?: boolean;
};

/**
 * Tap-to-open native date picker, replacing a free-text "yyyy-aa-gg" box (D-mobil-005). Android
 * only for now: this app's dev target is the Android emulator (see README's "Android development
 * build" - there is no documented/tested iOS dev flow yet), and `DateTimePickerAndroid`'s imperative
 * API is the library's own recommended approach on Android (its README: "we recommend using the
 * imperative API on Android"). On any other platform the field shows the value but tapping is a
 * no-op until an iOS picker is designed. Request forms use the JS `SheetDateField` instead.
 */
export function DateField({ value, onChange, minimumDateValue, disabled, label, required }: Props) {
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
    <View style={styles.container}>
      {label ? (
        <Text style={styles.label}>
          {label}
          {required ? " *" : ""}
        </Text>
      ) : null}
      <TouchableOpacity
        style={[styles.field, disabled && styles.fieldDisabled]}
        onPress={open}
        disabled={disabled}
        accessibilityRole="button"
        accessibilityLabel={label ?? "Tarih"}
        accessibilityState={{ disabled }}
      >
        <Text style={styles.fieldText}>{formatDateWithWeekday(value)}</Text>
        <Ionicons name="calendar-outline" size={18} color={palette.textFaint} />
      </TouchableOpacity>
    </View>
  );
}
