import React from "react";
import { Platform, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { DateTimePickerAndroid } from "@react-native-community/datetimepicker";
import { dateToTimeOnly, timeOnlyToDate } from "@/lib/formatters";
import { palette } from "@/theme/theme";
import { pickerFieldStyles as styles } from "./fieldStyles";

type Props = {
  /** `TimeOnly`-shaped "HH:mm". */
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  /** Caption shown above the glass box (and used as its accessible name). */
  label?: string;
};

/** Tap-to-open native time picker for hourly leave start/end times; see `DateField` for the Android-only rationale. */
export function TimeField({ value, onChange, disabled, label }: Props) {
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
    <View style={styles.container}>
      {label ? <Text style={styles.label}>{label}</Text> : null}
      <TouchableOpacity
        style={[styles.field, disabled && styles.fieldDisabled]}
        onPress={open}
        disabled={disabled}
        accessibilityRole="button"
        accessibilityLabel={label ?? "Saat"}
        accessibilityState={{ disabled }}
      >
        <Text style={styles.fieldText}>{value}</Text>
        <Ionicons name="time-outline" size={18} color={palette.textFaint} />
      </TouchableOpacity>
    </View>
  );
}
