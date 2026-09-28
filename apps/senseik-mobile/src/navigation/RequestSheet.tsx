import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { BottomSheet, IconCircleButton } from "@/components/ui";
import { REQUEST_OPTIONS } from "@/lib/requestOptions";
import { accents, fontFor, palette, text } from "@/theme/theme";
import type { RequestKind } from "@/types/navigation";

type Props = {
  visible: boolean;
  onClose: () => void;
  onSelect: (kind: RequestKind) => void;
};

/** "Yeni talep" bottom sheet opened by the tab bar's FAB. The sheet's Modal covers the tab bar (so
 * the FAB's "+" -> "x" turn is hidden while it is open); the top-right "x" is the visible close. */
export function RequestSheet({ visible, onClose, onSelect }: Props) {
  const { t } = useTranslation("requests");
  return (
    <BottomSheet visible={visible} onClose={onClose}>
      <View style={styles.header}>
        <Text style={styles.title}>{t("sheet.title")}</Text>
        <IconCircleButton icon="close" onPress={onClose} accessibilityLabel={t("sheet.close")} />
      </View>
      <Text style={styles.subtitle}>{t("sheet.subtitle")}</Text>
      {REQUEST_OPTIONS.map((option, index) => {
        const accent = accents[option.accent];
        const isLast = index === REQUEST_OPTIONS.length - 1;
        return (
          <TouchableOpacity
            key={option.kind}
            style={[styles.row, !isLast && styles.rowDivider]}
            onPress={() => onSelect(option.kind)}
            accessibilityRole="button"
            accessibilityLabel={t(option.labelKey)}
            accessibilityHint={t(option.descriptionKey)}
          >
            <View style={[styles.icon, { backgroundColor: accent.bg }]}>
              <Ionicons name={option.icon} size={22} color={accent.fg} />
            </View>
            <View style={styles.texts}>
              <Text style={styles.label}>{t(option.labelKey)}</Text>
              <Text style={styles.description}>{t(option.descriptionKey)}</Text>
            </View>
            <Ionicons name="chevron-forward" size={20} color={palette.textMuted} />
          </TouchableOpacity>
        );
      })}
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  header: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    // Pulls the 40px button's extra height out so the title keeps BottomSheet's own title spacing.
    marginTop: -8,
    marginRight: -8,
    marginBottom: -2,
  },
  title: {
    ...text.heading,
    flex: 1,
  },
  subtitle: {
    ...text.bodyMuted,
    fontSize: 13,
    marginBottom: 10,
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: 14,
    paddingVertical: 12,
    paddingHorizontal: 4,
  },
  rowDivider: {
    borderBottomWidth: 1,
    borderBottomColor: palette.line,
  },
  icon: {
    width: 44,
    height: 44,
    borderRadius: 14,
    alignItems: "center",
    justifyContent: "center",
  },
  texts: {
    flex: 1,
  },
  label: {
    ...text.body,
    ...fontFor(800),
  },
  description: {
    ...text.caption,
    fontSize: 12.5,
  },
});
