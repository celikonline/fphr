import React from "react";
import { StyleSheet, Text, View } from "react-native";
import { fontFor, palette, radius } from "@/theme/theme";

type Props = {
  title: string;
  body: string;
};

/** Foreground in-app banner shown when a push notification arrives while the app is open.
 * Purely presentational - App.tsx owns show/hide timing, auto-dismiss and tap-to-navigate. */
export function PushBanner({ title, body }: Props) {
  return (
    <View style={styles.banner} pointerEvents="none" accessibilityRole="alert">
      {title.length > 0 && <Text style={styles.title}>{title}</Text>}
      {body.length > 0 && <Text style={styles.body}>{body}</Text>}
    </View>
  );
}

const styles = StyleSheet.create({
  banner: {
    position: "absolute",
    top: 48,
    left: 16,
    right: 16,
    zIndex: 100,
    backgroundColor: palette.sheetTop,
    borderRadius: radius.card,
    borderWidth: 1,
    borderColor: palette.glassBorder,
    padding: 12,
  },
  title: {
    color: palette.white,
    ...fontFor(700),
    marginBottom: 2,
  },
  body: {
    color: palette.textMuted,
    ...fontFor(500),
  },
});
