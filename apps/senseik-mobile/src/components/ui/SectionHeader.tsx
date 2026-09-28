import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { useTranslation } from "react-i18next";
import { fontFor, palette, text } from "@/theme/theme";
import { toLocaleUpper } from "@/lib/formatters";

type Props = {
  title: string;
  actionLabel?: string;
  onAction?: () => void;
};

/**
 * Uppercases a section title with the active app language's casing rules (not the device's
 * default locale) so in Turkish "İşlemlerim" becomes "İŞLEMLERİM" (not "İŞLEMLERIM") and "izinde"
 * becomes "İZİNDE" (not "IZINDE") regardless of the device's system locale.
 */
export function sectionTitleText(title: string): string {
  return toLocaleUpper(title);
}

/** Section title (rendered uppercase via `sectionTitleText`) with an optional right-aligned text action ("Tümü", "Geçmiş", ...). */
export function SectionHeader({ title, actionLabel, onAction }: Props) {
  // Re-renders on a language switch so the upper-cased title follows the new locale.
  useTranslation();
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
