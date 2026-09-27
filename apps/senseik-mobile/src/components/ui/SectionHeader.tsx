import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { fontFor, palette, text } from "@/theme/theme";

type Props = {
  title: string;
  actionLabel?: string;
  onAction?: () => void;
};

/**
 * Uppercases a section title with Turkish casing rules ("tr-TR" locale, not the device's default
 * one) so "İşlemlerim" becomes "İŞLEMLERİM" (not "İŞLEMLERIM") and "izinde" becomes "İZİNDE" (not
 * "IZINDE") regardless of the device's system locale.
 */
export function sectionTitleText(title: string): string {
  return title.toLocaleUpperCase("tr-TR");
}

/** Section title (rendered uppercase via `sectionTitleText`) with an optional right-aligned text action ("Tümü", "Geçmiş", ...). */
export function SectionHeader({ title, actionLabel, onAction }: Props) {
  return (
    <View style={styles.container}>
      <Text style={styles.title}>{sectionTitleText(title)}</Text>
      {actionLabel && onAction ? (
        <TouchableOpacity
          onPress={onAction}
          accessibilityRole="button"
          accessibilityLabel={actionLabel}
        >
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
    color: palette.brandText,
    ...fontFor(700),
  },
});
