import React, { useEffect } from "react";
import { StyleSheet, View } from "react-native";
import Animated, {
  useAnimatedStyle,
  useReducedMotion,
  useSharedValue,
  withRepeat,
  withSequence,
  withTiming,
} from "react-native-reanimated";
import { backdropImage, palette } from "@/theme/theme";

const FLOAT_HALF_DURATION_MS = 3500;
const FLOAT_DISTANCE = 12;
const GHOST_SIZE = 320;
// borderRadius ~26% of size for the rounded-square brand shape at this scale.
const GHOST_RADIUS = 84;

type Props = {
  children?: React.ReactNode;
};

/**
 * Full-bleed dark gradient with two drifting, oversized brand-mark ghosts tucked into the
 * top-right hero region (kept clear of the glass form panel below, so they never crowd the
 * password field). The decorative layer is `pointerEvents="none"` so it never intercepts taps
 * meant for the form rendered as `children` on top of it.
 */
export function AuthBackdrop({ children }: Props) {
  const reduceMotion = useReducedMotion();
  const drift = useSharedValue(0);

  useEffect(() => {
    if (reduceMotion) return;
    drift.value = withRepeat(
      withSequence(
        withTiming(-FLOAT_DISTANCE, { duration: FLOAT_HALF_DURATION_MS }),
        withTiming(FLOAT_DISTANCE, { duration: FLOAT_HALF_DURATION_MS })
      ),
      -1,
      true
    );
  }, [reduceMotion, drift]);

  const driftStyle = useAnimatedStyle(() => ({ transform: [{ translateY: drift.value }] }));

  return (
    <View style={styles.container}>
      <View style={StyleSheet.absoluteFill} pointerEvents="none">
        <View style={styles.gradient} />
        <Animated.View style={[styles.ghostOutlineWrapper, driftStyle]}>
          <View style={styles.ghostOutline} />
        </Animated.View>
        <Animated.View style={[styles.ghostFilledWrapper, driftStyle]}>
          <View style={styles.ghostFilled} />
        </Animated.View>
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
  ghostOutlineWrapper: {
    position: "absolute",
    width: GHOST_SIZE,
    height: GHOST_SIZE,
    top: -70,
    right: -150,
  },
  ghostFilledWrapper: {
    position: "absolute",
    width: GHOST_SIZE,
    height: GHOST_SIZE,
    top: 90,
    right: -190,
  },
  ghostOutline: {
    width: "100%",
    height: "100%",
    borderRadius: GHOST_RADIUS,
    transform: [{ rotate: "14deg" }],
    borderWidth: 1,
    borderColor: palette.ghostBorder,
    backgroundColor: palette.ghostOutlineFill,
  },
  ghostFilled: {
    width: "100%",
    height: "100%",
    borderRadius: GHOST_RADIUS,
    transform: [{ rotate: "14deg" }],
    backgroundColor: palette.brand,
    opacity: 0.3,
  },
});
