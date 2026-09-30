import React from "react";
import { StyleSheet } from "react-native";
import Animated, { FadeInDown } from "react-native-reanimated";
import { palette, radius } from "@/theme/theme";

/** The glass card of the auth screens (login, tenant select, MFA, forgot password, biometric),
 * with the same entrance animation everywhere. */
export function AuthPanel({ children }: { children: React.ReactNode }) {
  return (
    <Animated.View entering={FadeInDown.delay(450).duration(600)} style={styles.panel}>
      {children}
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  panel: {
    marginTop: 28,
    padding: 24,
    borderRadius: radius.sheet,
    backgroundColor: palette.glass,
    borderWidth: 1,
    borderColor: palette.glassBorder,
  },
});
