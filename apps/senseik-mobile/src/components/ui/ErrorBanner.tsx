import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { palette, radius, text } from "@/theme/theme";

type Props = {
  message: string;
  onRetry?: () => void;
};

/** Inline error state for a failed section load, with an optional "Tekrar dene" retry action. */
export function ErrorBanner({ message, onRetry }: Props) {
  return (
    <View style={styles.container} accessibilityRole="alert">
      <Ionicons name="alert-circle-outline" size={18} color={palette.coral} />
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
    backgroundColor: "rgba(255,138,128,0.12)",
    borderWidth: 1,
    borderColor: "rgba(255,138,128,0.3)",
  },
  message: {
    ...text.bodyMuted,
    flex: 1,
    color: palette.coral,
  },
  retry: {
    fontSize: 13,
    fontWeight: "700",
    color: palette.brandText,
  },
});
