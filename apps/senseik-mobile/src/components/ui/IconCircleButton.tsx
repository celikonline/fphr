import React from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { fontFor, palette, radius } from "@/theme/theme";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];

type Props = {
  icon: IoniconName;
  onPress: () => void;
  accessibilityLabel: string;
  badge?: string;
};

/** Small glass icon button used in the top bar (calendar, bell, overflow menu). */
export function IconCircleButton({ icon, onPress, accessibilityLabel, badge }: Props) {
  return (
    <TouchableOpacity
      style={styles.button}
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel}
      // Visual size stays 40x40 (mockup's `.icon-btn`); hitSlop brings the tappable area to >=44.
      hitSlop={{ top: 2, bottom: 2, left: 2, right: 2 }}
    >
      <Ionicons name={icon} size={22} color={palette.ice} />
      {badge ? (
        <View style={styles.badge}>
          <Text style={styles.badgeText}>{badge}</Text>
        </View>
      ) : null}
    </TouchableOpacity>
  );
}

const styles = StyleSheet.create({
  button: {
    width: 40,
    height: 40,
    borderRadius: radius.button,
    alignItems: "center",
    justifyContent: "center",
  },
  badge: {
    position: "absolute",
    top: 3,
    right: 1,
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
    ...fontFor(800),
  },
});
