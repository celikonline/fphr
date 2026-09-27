import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { palette, text } from "@/theme/theme";

type Props = {
  title: string;
  actionLabel?: string;
  onAction?: () => void;
};

/** Uppercase section title with an optional right-aligned text action ("Tümü", "Geçmiş", ...). */
export function SectionHeader({ title, actionLabel, onAction }: Props) {
  return (
    <View style={styles.container}>
      <Text style={styles.title}>{title}</Text>
      {actionLabel && onAction ? (
        <TouchableOpacity onPress={onAction} accessibilityRole="button" accessibilityLabel={actionLabel}>
          <Text style={styles.action}>{actionLabel}</Text>
        </TouchableOpacity>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "baseline",
    paddingHorizontal: 20,
    paddingTop: 22,
    paddingBottom: 10,
  },
  title: {
    ...text.section,
  },
  action: {
    fontSize: 13,
    fontWeight: "700",
    color: palette.brandText,
  },
});
