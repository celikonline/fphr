import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { fontFor, palette, radius, text } from "@/theme/theme";

type Option<T extends string> = { value: T; label: string };

type Props<T extends string> = {
  options: Option<T>[];
  value: T;
  onChange: (value: T) => void;
};

export function SegmentedControl<T extends string>({ options, value, onChange }: Props<T>) {
  return (
    <View style={styles.container}>
      {options.map((option) => {
        const active = option.value === value;
        return (
          <TouchableOpacity
            key={option.value}
            style={[styles.segment, active && styles.segmentActive]}
            onPress={() => onChange(option.value)}
            accessibilityRole="button"
            accessibilityState={{ selected: active }}
            accessibilityLabel={option.label}
          >
            <Text style={[styles.label, active && styles.labelActive]}>{option.label}</Text>
          </TouchableOpacity>
        );
      })}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flexDirection: "row",
    gap: 4,
    padding: 4,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    borderRadius: radius.button,
  },
  segment: {
    flex: 1,
    paddingVertical: 10,
    borderRadius: radius.button - 4,
    alignItems: "center",
    justifyContent: "center",
  },
  segmentActive: {
    backgroundColor: palette.brand,
  },
  label: {
    ...text.bodyMuted,
  },
  labelActive: {
    color: palette.white,
    ...fontFor(700),
  },
});
