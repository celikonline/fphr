import React, { type ReactNode } from "react";
import { StyleSheet, TouchableOpacity, View, type StyleProp, type ViewStyle } from "react-native";
import { palette, radius } from "@/theme/theme";

type Props = {
  children: ReactNode;
  onPress?: () => void;
  style?: StyleProp<ViewStyle>;
  accessibilityLabel?: string;
  padding?: number;
};

/** The frosted-glass card surface used across the redesign (balances, action tiles, list rows). */
export function GlassCard({ children, onPress, style, accessibilityLabel, padding = 16 }: Props) {
  if (onPress) {
    return (
      <TouchableOpacity
        style={[styles.card, { padding }, style]}
        onPress={onPress}
        accessibilityRole="button"
        accessibilityLabel={accessibilityLabel}
      >
        {children}
      </TouchableOpacity>
    );
  }

  return <View style={[styles.card, { padding }, style]}>{children}</View>;
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    borderRadius: radius.card,
  },
});
