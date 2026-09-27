import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { accents, palette, radius, text, type AccentKey } from "@/theme/theme";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];

type Props = {
  label: string;
  icon: IoniconName;
  accent: AccentKey;
  badge?: number;
  onPress: () => void;
};

/** A single tile in the home screen's "İşlemlerim" grid. */
export function ActionTile({ label, icon, accent, badge, onPress }: Props) {
  const accentStyle = accents[accent];
  const showBadge = typeof badge === "number" && badge > 0;

  return (
    <TouchableOpacity
      style={styles.tile}
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={label}
    >
      <View style={[styles.iconWrap, { backgroundColor: accentStyle.bg }]}>
        <Ionicons name={icon} size={24} color={accentStyle.fg} />
        {showBadge ? (
          <View style={styles.badge}>
            <Text style={styles.badgeText}>{badge > 9 ? "9+" : String(badge)}</Text>
          </View>
        ) : null}
      </View>
      <Text style={styles.label} numberOfLines={1}>
        {label}
      </Text>
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  tile: {
    flex: 1,
    aspectRatio: 1 / 0.92,
    borderRadius: radius.tile,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    alignItems: "center",
    justifyContent: "center",
    gap: 10,
  },
  iconWrap: {
    width: 46,
    height: 46,
    borderRadius: 15,
    alignItems: "center",
    justifyContent: "center",
  },
  badge: {
    position: "absolute",
    top: -6,
    right: -8,
    minWidth: 18,
    height: 18,
    paddingHorizontal: 5,
    borderRadius: 9,
    backgroundColor: palette.badge,
    borderWidth: 2,
    borderColor: palette.night,
    alignItems: "center",
    justifyContent: "center",
  },
  badgeText: {
    color: palette.white,
    fontSize: 10,
    fontWeight: "800",
  },
  label: {
    ...text.caption,
    color: palette.white,
    fontWeight: "700",
  },
});
