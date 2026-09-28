import React from "react";
import { Modal, Pressable, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { useReducedMotion } from "react-native-reanimated";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useTranslation } from "react-i18next";
import { fontFor, palette } from "@/theme/theme";

export type HomeMenuAction = "profile" | "notificationPreferences" | "support" | "logout";

type Props = {
  visible: boolean;
  onClose: () => void;
  onSelect: (action: HomeMenuAction) => void;
};

const ITEMS: Array<{ action: HomeMenuAction; labelKey: string }> = [
  { action: "profile", labelKey: "menu.profile" },
  { action: "notificationPreferences", labelKey: "menu.notificationPreferences" },
  { action: "support", labelKey: "menu.support" },
  { action: "logout", labelKey: "menu.logout" },
];

/** Just below the top bar's 40px icon buttons (header row: 8px top padding + 40 + 4). */
const MENU_OFFSET_FROM_SAFE_TOP = 52;

/**
 * The ⋮ overflow menu (mockup `.menu`): a glass panel under the top-right buttons. Tapping outside
 * or the Android back button closes it; picking an item closes it and reports the choice.
 */
export function HomeMenu({ visible, onClose, onSelect }: Props) {
  const { t } = useTranslation("home");
  const insets = useSafeAreaInsets();
  const reduceMotion = useReducedMotion();

  return (
    <Modal
      visible={visible}
      transparent
      animationType={reduceMotion ? "none" : "fade"}
      statusBarTranslucent
      onRequestClose={onClose}
    >
      <Pressable
        style={StyleSheet.absoluteFill}
        onPress={onClose}
        accessibilityRole="button"
        accessibilityLabel={t("menu.close")}
      />
      <View
        style={[styles.menu, { top: insets.top + MENU_OFFSET_FROM_SAFE_TOP }]}
        accessibilityRole="menu"
      >
        {ITEMS.map((item) => (
          <TouchableOpacity
            key={item.action}
            style={styles.item}
            onPress={() => {
              onClose();
              onSelect(item.action);
            }}
            accessibilityRole="menuitem"
            accessibilityLabel={t(item.labelKey)}
          >
            <Text style={[styles.label, item.action === "logout" && styles.danger]}>
              {t(item.labelKey)}
            </Text>
          </TouchableOpacity>
        ))}
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  menu: {
    position: "absolute",
    right: 16,
    minWidth: 210,
    padding: 6,
    borderRadius: 18,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    backgroundColor: palette.sheetTop,
    shadowColor: palette.night,
    shadowOffset: { width: 0, height: 18 },
    shadowOpacity: 0.4,
    shadowRadius: 20,
    elevation: 12,
  },
  item: {
    minHeight: 44,
    justifyContent: "center",
    paddingHorizontal: 12,
    borderRadius: 12,
  },
  label: {
    fontSize: 14,
    color: palette.white,
    ...fontFor(700),
  },
  danger: {
    color: palette.coral,
  },
});
