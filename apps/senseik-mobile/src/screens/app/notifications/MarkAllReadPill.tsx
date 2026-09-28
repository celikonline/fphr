import React from "react";
import { StyleSheet, Text, TouchableOpacity } from "react-native";
import { useTranslation } from "react-i18next";
import { fontFor, palette, radius } from "@/theme/theme";

type Props = {
  disabled: boolean;
  onPress: () => void;
};

/** "Tümünü okundu yap" hap: `ScreenHeader`'ın `right` yuvasında gösterilir. */
export function MarkAllReadPill({ disabled, onPress }: Props) {
  const { t } = useTranslation("notifications");
  return (
    <TouchableOpacity
      style={[styles.pill, disabled && styles.pillDisabled]}
      onPress={onPress}
      disabled={disabled}
      accessibilityRole="button"
      accessibilityLabel={t("markAllRead")}
      // Görsel yükseklik ~26px (paddingVertical 6 + 16px satır yüksekliği) - hitSlop dokunma alanını
      // >=44'e çıkarır (ScreenHeader/IconCircleButton'daki aynı desen).
      hitSlop={{ top: 10, bottom: 10, left: 10, right: 10 }}
    >
      <Text style={[styles.text, disabled && styles.textDisabled]}>{t("markAllRead")}</Text>
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  pill: {
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: radius.pill,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  pillDisabled: {
    opacity: 0.5,
  },
  text: {
    fontSize: 12,
    lineHeight: 16,
    color: palette.brandText,
    ...fontFor(700),
  },
  textDisabled: {
    color: palette.textFaint,
  },
});
