import React, { useEffect, useState } from "react";
import { Pressable, StyleSheet, Text, useWindowDimensions, View } from "react-native";
import type { BottomTabBarProps } from "@react-navigation/bottom-tabs";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import Svg, { Path } from "react-native-svg";
import Animated, {
  useAnimatedStyle,
  useReducedMotion,
  useSharedValue,
  withSpring,
} from "react-native-reanimated";
import { Ionicons } from "@expo/vector-icons";
import { fontFor, palette } from "@/theme/theme";
import { buildNotchPath, FAB_SIZE, TAB_BAR_HEIGHT } from "@/navigation/layout";
import { RequestSheet } from "@/navigation/RequestSheet";
import type { AppStackParamList, MainTabsParamList, RequestKind } from "@/types/navigation";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];

const TABS: Record<
  keyof MainTabsParamList,
  { label: string; icon: IoniconName; activeIcon: IoniconName; side: "left" | "right" }
> = {
  Home: { label: "Ana sayfa", icon: "grid-outline", activeIcon: "grid", side: "left" },
  CheckIn: {
    label: "Giriş / Çıkış",
    icon: "stopwatch-outline",
    activeIcon: "stopwatch",
    side: "right",
  },
};

/** Mockup `.tabbar path` stroke - slightly brighter than `palette.line` so the notch edge reads. */
const BAR_BORDER = "rgba(255,255,255,0.12)";
/** Mockup `.tab.left{left:62px}` + half its 64px width: the tab's centre sits 94px from the edge. */
const TAB_CENTRE_INSET = 94;
const TAB_WIDTH = 96;
/** Mockup `.fab{top:-20px}`: the FAB rises above the bar into the notch. */
const FAB_RISE = 20;
const FAB_SQUARE = 54;
const FAB_OPEN_ROTATION_DEG = 45;

/**
 * Custom bottom bar (spec §3): an opaque dark bar with a notch cut around the centre FAB (the brand
 * mark's two overlapping squares), "Ana sayfa" on the left and "Giriş / Çıkış" on the right. The FAB
 * is not a route - it opens the "Yeni talep" sheet, and picking a type opens `NewRequest`.
 */
export function NotchedTabBar({ state, navigation }: BottomTabBarProps) {
  const insets = useSafeAreaInsets();
  const { width } = useWindowDimensions();
  const [sheetOpen, setSheetOpen] = useState(false);
  const height = TAB_BAR_HEIGHT + insets.bottom;

  function openRequest(kind: RequestKind) {
    setSheetOpen(false);
    const params: AppStackParamList["NewRequest"] = { kind };
    // `NewRequest` lives in the parent AppStack; react-navigation bubbles the action up to it.
    navigation.navigate("NewRequest", params);
  }

  return (
    <View style={[styles.bar, { height }]} accessibilityRole="tablist">
      <Svg width={width} height={height} style={StyleSheet.absoluteFill}>
        <Path
          d={buildNotchPath(width, height)}
          fill={palette.tabBar}
          stroke={BAR_BORDER}
          strokeWidth={1}
        />
      </Svg>

      {state.routes.map((route, index) => {
        const tab = TABS[route.name as keyof MainTabsParamList];
        if (!tab) return null;
        const isFocused = state.index === index;

        function onPress() {
          const event = navigation.emit({
            type: "tabPress",
            target: route.key,
            canPreventDefault: true,
          });
          if (!isFocused && !event.defaultPrevented) {
            navigation.navigate(route.name, route.params);
          }
        }

        return (
          <Pressable
            key={route.key}
            onPress={onPress}
            style={[
              styles.tab,
              tab.side === "left"
                ? { left: TAB_CENTRE_INSET - TAB_WIDTH / 2 }
                : { right: TAB_CENTRE_INSET - TAB_WIDTH / 2 },
            ]}
            accessibilityRole="tab"
            accessibilityLabel={tab.label}
            accessibilityState={{ selected: isFocused }}
          >
            <Ionicons
              name={isFocused ? tab.activeIcon : tab.icon}
              size={26}
              color={isFocused ? palette.brand : palette.textFaint}
            />
            <Text
              style={[styles.tabLabel, { color: isFocused ? palette.white : palette.textMuted }]}
              numberOfLines={1}
            >
              {tab.label}
            </Text>
          </Pressable>
        );
      })}

      <RequestFab
        left={(width - FAB_SIZE) / 2}
        open={sheetOpen}
        onPress={() => setSheetOpen(true)}
      />

      <RequestSheet
        visible={sheetOpen}
        onClose={() => setSheetOpen(false)}
        onSelect={openRequest}
      />
    </View>
  );
}

type FabProps = { left: number; open: boolean; onPress: () => void };

/** The brand mark as a button: white square top-right, blue square bottom-left carrying the `+`,
 * which turns into an `×` (45°) while the sheet is open. */
function RequestFab({ left, open, onPress }: FabProps) {
  const reduceMotion = useReducedMotion();
  const rotation = useSharedValue(0);

  useEffect(() => {
    const target = open ? FAB_OPEN_ROTATION_DEG : 0;
    rotation.value = reduceMotion ? target : withSpring(target);
  }, [open, reduceMotion, rotation]);

  const iconStyle = useAnimatedStyle(() => ({
    transform: [{ rotate: `${rotation.value}deg` }],
  }));

  return (
    <Pressable
      onPress={onPress}
      style={({ pressed }) => [styles.fab, { left }, pressed && styles.fabPressed]}
      accessibilityRole="button"
      accessibilityLabel="Yeni talep oluştur"
      accessibilityState={{ expanded: open }}
    >
      <View style={[styles.fabSquare, styles.fabBack]} />
      <View style={[styles.fabSquare, styles.fabFront]}>
        <Animated.View style={iconStyle}>
          <Ionicons name="add" size={30} color={palette.white} />
        </Animated.View>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  bar: {
    position: "absolute",
    left: 0,
    right: 0,
    bottom: 0,
  },
  tab: {
    position: "absolute",
    top: 10,
    width: TAB_WIDTH,
    minHeight: 48,
    alignItems: "center",
    gap: 4,
  },
  tabLabel: {
    fontSize: 11,
    lineHeight: 14,
    ...fontFor(700),
  },
  fab: {
    position: "absolute",
    top: -FAB_RISE,
    width: FAB_SIZE,
    height: FAB_SIZE,
  },
  fabPressed: {
    transform: [{ scale: 0.95 }],
  },
  fabSquare: {
    position: "absolute",
    width: FAB_SQUARE,
    height: FAB_SQUARE,
    borderRadius: 17,
  },
  fabBack: {
    right: 0,
    top: 0,
    backgroundColor: palette.white,
  },
  fabFront: {
    left: 0,
    bottom: 0,
    backgroundColor: palette.brand,
    alignItems: "center",
    justifyContent: "center",
    shadowColor: palette.brand,
    shadowOpacity: 0.55,
    shadowRadius: 12,
    shadowOffset: { width: 0, height: 10 },
    elevation: 8,
  },
});
