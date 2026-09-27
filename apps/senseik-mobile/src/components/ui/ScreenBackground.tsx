import React, { type ReactNode } from "react";
import { StyleSheet, View } from "react-native";
import { backdropImage, palette } from "@/theme/theme";

type Props = {
  children?: ReactNode;
};

const GHOST_RADIUS = 56;

/**
 * Full-bleed dark gradient with two static "ghost square" traces of the brand mark, mirroring
 * `AuthBackdrop`'s border/fill values so every screen shares the same backdrop. Purely decorative -
 * `pointerEvents="none"` so it never intercepts taps meant for `children`.
 */
export function ScreenBackground({ children }: Props) {
  return (
    <View style={styles.container}>
      <View style={StyleSheet.absoluteFill} pointerEvents="none">
        <View style={styles.gradient} />
        <View style={styles.ghostOutline} />
        <View style={styles.ghostFilled} />
      </View>
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: palette.night,
  },
  gradient: {
    ...StyleSheet.absoluteFill,
    backgroundColor: palette.night,
    // New-Architecture-only style prop (RN 0.86); falls back to the solid `backgroundColor` above
    // on runtimes that don't understand it.
    experimental_backgroundImage: backdropImage,
  },
  ghostOutline: {
    position: "absolute",
    width: 260,
    height: 260,
    top: 120,
    right: -110,
    borderRadius: GHOST_RADIUS,
    transform: [{ rotate: "-14deg" }],
    borderWidth: 1,
    borderColor: "rgba(255,255,255,0.18)",
    backgroundColor: "rgba(255,255,255,0.07)",
  },
  ghostFilled: {
    position: "absolute",
    width: 200,
    height: 200,
    top: 520,
    left: -120,
    borderRadius: 44,
    transform: [{ rotate: "18deg" }],
    backgroundColor: "rgba(0,140,240,0.10)",
  },
});
