import React, { useEffect } from "react";
import { StyleSheet, View } from "react-native";
import Animated, {
  useAnimatedStyle,
  useReducedMotion,
  useSharedValue,
  withDelay,
  withSpring,
  withTiming,
} from "react-native-reanimated";
import { palette } from "@/theme/theme";

const BASE_SIZE = 40;
const SPRING_DAMPING = 14;
const ARROW_FADE_DELAY_MS = 350;

type Props = {
  size?: number;
  /** Set to false to render the settled mark with no entrance animation. */
  animated?: boolean;
};

/**
 * Two overlapping rounded squares echoing the web wordmark's signature glyph. Purely decorative
 * (login/tenant-select hero) — never conveys information on its own, so it stays out of the
 * accessibility tree.
 */
export function BrandMark({ size = 56, animated = true }: Props) {
  const reduceMotion = useReducedMotion();
  const playAnimation = animated && !reduceMotion;
  const unit = size / BASE_SIZE;

  const progress = useSharedValue(playAnimation ? 0 : 1);
  const arrowOpacity = useSharedValue(playAnimation ? 0 : 1);

  useEffect(() => {
    if (!playAnimation) return;
    progress.value = withSpring(1, { damping: SPRING_DAMPING });
    arrowOpacity.value = withDelay(ARROW_FADE_DELAY_MS, withTiming(1));
  }, [playAnimation, progress, arrowOpacity]);

  const backSquareStyle = useAnimatedStyle(() => ({
    opacity: progress.value,
    transform: [
      { translateX: (1 - progress.value) * size * 0.35 },
      { translateY: (1 - progress.value) * -size * 0.35 },
    ],
  }));

  const frontSquareStyle = useAnimatedStyle(() => ({
    opacity: progress.value,
    transform: [
      { translateX: (1 - progress.value) * -size * 0.35 },
      { translateY: (1 - progress.value) * size * 0.35 },
    ],
  }));

  const arrowStyle = useAnimatedStyle(() => ({ opacity: arrowOpacity.value }));

  return (
    <View
      style={{ width: size, height: size }}
      accessible={false}
      importantForAccessibility="no-hide-descendants"
    >
      <Animated.View
        style={[
          styles.square,
          backSquareStyle,
          {
            left: 12 * unit,
            top: 4 * unit,
            width: 24 * unit,
            height: 24 * unit,
            borderRadius: 6 * unit,
            backgroundColor: palette.white,
          },
        ]}
      />
      <Animated.View
        style={[
          styles.square,
          styles.frontSquare,
          frontSquareStyle,
          {
            left: 4 * unit,
            top: 12 * unit,
            width: 24 * unit,
            height: 24 * unit,
            borderRadius: 6 * unit,
            backgroundColor: palette.brand,
          },
        ]}
      >
        <Animated.Text
          style={[styles.arrow, arrowStyle, { fontSize: 19 * unit, lineHeight: 19 * unit }]}
        >
          ↗
        </Animated.Text>
      </Animated.View>
    </View>
  );
}

const styles = StyleSheet.create({
  square: {
    position: "absolute",
  },
  frontSquare: {
    alignItems: "center",
    justifyContent: "center",
  },
  arrow: {
    color: palette.white,
    // Deliberately `fontWeight`, not `fontFor`: Manrope has no "↗" (U+2197) glyph, so the arrow
    // always renders in the system fallback font, where only a numeric weight makes it heavy.
    fontWeight: "900",
    textAlign: "center",
    includeFontPadding: false,
  },
});
