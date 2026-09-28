import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { palette, radius, text } from "@/theme/theme";

type Props = {
  label: string;
  onPrev: () => void;
  onNext: () => void;
  prevLabel: string;
  nextLabel: string;
};

/** "‹ Eylül 2026 ›" period switcher (Puantaj months, Vardiyam weeks). */
export function PeriodStepper({ label, onPrev, onNext, prevLabel, nextLabel }: Props) {
  return (
    <View style={styles.container}>
      <StepButton icon="chevron-back" onPress={onPrev} accessibilityLabel={prevLabel} />
      <Text style={styles.label} numberOfLines={1}>
        {label}
      </Text>
      <StepButton icon="chevron-forward" onPress={onNext} accessibilityLabel={nextLabel} />
    </View>
  );
}

function StepButton({
  icon,
  onPress,
  accessibilityLabel,
}: {
  icon: "chevron-back" | "chevron-forward";
  onPress: () => void;
  accessibilityLabel: string;
}) {
  return (
    <TouchableOpacity
      style={styles.button}
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
    >
      <Ionicons name={icon} size={20} color={palette.white} />
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: 8,
  },
  button: {
    width: 44,
    height: 44,
    borderRadius: radius.button,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  label: {
    ...text.heading,
    flexShrink: 1,
    textAlign: "center",
  },
});
