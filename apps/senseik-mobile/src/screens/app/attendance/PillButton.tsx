import React from "react";
import { StyleSheet, Text, TouchableOpacity } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { fontFor, palette, radius, text } from "@/theme/theme";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];

type Props = {
  icon: IoniconName;
  label: string;
  onPress: () => void;
  /** Brand-blue fill (e.g. a filter other than "Tümü" is on). */
  active?: boolean;
};

/** Glass pill with an icon (Puantaj's "Filtre" and its Giriş / Çıkış, Vardiyam shortcuts). */
export function PillButton({ icon, label, onPress, active = false }: Props) {
  return (
    <TouchableOpacity
      style={[styles.pill, active && styles.pillActive]}
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={label}
    >
      <Ionicons name={icon} size={16} color={active ? palette.white : palette.ice} />
      <Text style={[styles.label, active && styles.labelActive]}>{label}</Text>
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  pill: {
    flexDirection: "row",
    alignItems: "center",
    gap: 6,
    minHeight: 44,
    paddingHorizontal: 14,
    borderRadius: radius.pill,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  pillActive: {
    backgroundColor: palette.brand,
    borderColor: palette.brand,
  },
  label: {
    ...text.caption,
    fontSize: 13,
    color: palette.white,
    ...fontFor(700),
  },
  labelActive: {
    color: palette.white,
  },
});
