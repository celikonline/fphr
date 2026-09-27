import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { fontFor, palette, radius, text, tones } from "@/theme/theme";

type Props = {
  message: string;
  onRetry?: () => void;
};

/** Inline error state for a failed section load, with an optional "Tekrar dene" retry action. */
export function ErrorBanner({ message, onRetry }: Props) {
  return (
    <View style={styles.container} accessibilityRole="alert">
      <Ionicons name="alert-circle-outline" size={18} color={tones.danger.fg} />
      <Text style={styles.message}>{message}</Text>
      {onRetry ? (
        <TouchableOpacity
          onPress={onRetry}
          accessibilityRole="button"
          accessibilityLabel="Tekrar dene"
        >
          <Text style={styles.retry}>Tekrar dene</Text>
        </TouchableOpacity>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flexDirection: "row",
    alignItems: "center",
    gap: 8,
    marginHorizontal: 20,
    padding: 14,
    borderRadius: radius.card,
    backgroundColor: tones.danger.bg,
    borderWidth: 1,
    borderColor: palette.dangerBorder,
  },
  message: {
    ...text.bodyMuted,
    flex: 1,
    color: tones.danger.fg,
  },
  retry: {
    fontSize: 13,
    color: palette.brandText,
    ...fontFor(700),
  },
});
