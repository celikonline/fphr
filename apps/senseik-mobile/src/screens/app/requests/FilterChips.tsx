import React from "react";
import { ScrollView, StyleSheet, Text, TouchableOpacity } from "react-native";
import { fontFor, palette, radius, text } from "@/theme/theme";

type Props<T extends string> = {
  options: { value: T; label: string }[];
  value: T;
  onChange: (value: T) => void;
};

/** Horizontally scrolling filter pills (Taleplerim's type filter). */
export function FilterChips<T extends string>({ options, value, onChange }: Props<T>) {
  return (
    <ScrollView
      horizontal
      showsHorizontalScrollIndicator={false}
      contentContainerStyle={styles.row}
    >
      {options.map((option) => {
        const active = option.value === value;
        return (
          <TouchableOpacity
            key={option.value}
            style={[styles.chip, active && styles.chipActive]}
            onPress={() => onChange(option.value)}
            accessibilityRole="button"
            accessibilityLabel={option.label}
            accessibilityState={{ selected: active }}
          >
            <Text style={[styles.label, active && styles.labelActive]}>{option.label}</Text>
          </TouchableOpacity>
        );
      })}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  row: {
    gap: 8,
    paddingHorizontal: 20,
    paddingVertical: 4,
  },
  chip: {
    minHeight: 44,
    justifyContent: "center",
    paddingHorizontal: 16,
    borderRadius: radius.pill,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  chipActive: {
    backgroundColor: palette.brand,
    borderColor: palette.brand,
  },
  label: {
    ...text.bodyMuted,
  },
  labelActive: {
    color: palette.white,
    ...fontFor(700),
  },
});
