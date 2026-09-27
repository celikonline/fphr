import React, { type ReactNode } from "react";
import { RefreshControl, ScrollView, StyleSheet, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { contentBottomInset } from "@/navigation/layout";
import { palette } from "@/theme/theme";
import { ScreenBackground } from "./ScreenBackground";

type Props = {
  children: ReactNode;
  /** Wraps `children` in a `ScrollView` with pull-to-refresh support. Defaults to true. */
  scroll?: boolean;
  refreshing?: boolean;
  onRefresh?: () => void;
  header?: ReactNode;
  /** Adds the tab bar's height (see `navigation/layout.ts`) to the bottom inset so content never
   * sits under the custom bottom bar. Defaults to false (stack screens with no tab bar). */
  withTabBar?: boolean;
  testID?: string;
};

/** Common screen shell: `ScreenBackground` + safe area + optional scroll/refresh + bottom inset. */
export function Screen({
  children,
  scroll = true,
  refreshing,
  onRefresh,
  header,
  withTabBar = false,
  testID,
}: Props) {
  const insets = useSafeAreaInsets();
  const bottomPadding = withTabBar ? contentBottomInset(insets.bottom) : insets.bottom + 16;

  return (
    <ScreenBackground>
      <View style={[styles.flex, { paddingTop: insets.top }]} testID={testID}>
        {header}
        {scroll ? (
          <ScrollView
            style={styles.flex}
            contentContainerStyle={{ paddingBottom: bottomPadding }}
            refreshControl={
              onRefresh ? (
                <RefreshControl
                  refreshing={Boolean(refreshing)}
                  onRefresh={onRefresh}
                  tintColor={palette.white}
                />
              ) : undefined
            }
          >
            {children}
          </ScrollView>
        ) : (
          <View style={[styles.flex, { paddingBottom: bottomPadding }]}>{children}</View>
        )}
      </View>
    </ScreenBackground>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
});
