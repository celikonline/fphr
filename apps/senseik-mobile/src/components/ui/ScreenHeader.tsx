import React, { type ReactNode } from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { useNavigation } from "@react-navigation/native";
import { Ionicons } from "@expo/vector-icons";
import { palette, radius, text } from "@/theme/theme";

type Props = {
  title: string;
  /** Defaults to `navigation.goBack()`. */
  onBack?: () => void;
  right?: ReactNode;
  subtitle?: string;
};

/** Shared stack-screen header: glass round back button + large title + optional right action. */
export function ScreenHeader({ title, onBack, right, subtitle }: Props) {
  const navigation = useNavigation();

  return (
    <View style={styles.container}>
      <TouchableOpacity
        style={styles.backButton}
        onPress={onBack ?? (() => navigation.goBack())}
        accessibilityRole="button"
        accessibilityLabel="Geri"
        // Visual size stays 40x40 (matches the top bar's glass icon buttons); hitSlop brings the
        // tappable area to >=44.
        hitSlop={{ top: 2, bottom: 2, left: 2, right: 2 }}
      >
        <Ionicons name="chevron-back" size={22} color={palette.white} />
      </TouchableOpacity>
      <View style={styles.titleWrap}>
        <Text style={styles.title} numberOfLines={1}>
          {title}
        </Text>
        {subtitle ? <Text style={styles.subtitle}>{subtitle}</Text> : null}
      </View>
      {right ? <View style={styles.right}>{right}</View> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flexDirection: "row",
    alignItems: "center",
    paddingHorizontal: 20,
    paddingVertical: 12,
    gap: 12,
  },
  backButton: {
    width: 40,
    height: 40,
    borderRadius: radius.button,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
  titleWrap: {
    flex: 1,
  },
  title: {
    ...text.title,
  },
  subtitle: {
    ...text.bodyMuted,
    marginTop: 2,
  },
  right: {
    marginLeft: "auto",
  },
});
