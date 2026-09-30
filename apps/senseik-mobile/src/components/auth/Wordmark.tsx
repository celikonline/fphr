import React from "react";
import { StyleSheet, Text } from "react-native";
import { fontFor, palette } from "@/theme/theme";

/** "SenseHR" wordmark of the auth screens - "HR" in the brand blue. */
export function Wordmark() {
  return (
    <Text style={styles.wordmark}>
      Sense
      <Text style={styles.accent}>HR</Text>
    </Text>
  );
}

const styles = StyleSheet.create({
  wordmark: {
    marginTop: 8,
    fontSize: 40,
    letterSpacing: -1.5,
    color: palette.white,
    ...fontFor(800),
  },
  accent: { color: palette.brand },
});
