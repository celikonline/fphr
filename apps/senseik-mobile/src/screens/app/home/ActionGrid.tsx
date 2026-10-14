import React, { useState } from "react";
import {
  FlatList,
  StyleSheet,
  View,
  useWindowDimensions,
  type NativeScrollEvent,
  type NativeSyntheticEvent,
} from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { ActionTile } from "@/components/ui";
import { palette, type AccentKey } from "@/theme/theme";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];

/** Tile targets: parameterless `AppStackParamList` screens (DashboardScreen navigates to them). */
export type ActionRoute =
  | "Attendance"
  | "Leave"
  | "Payroll"
  | "Approvals"
  | "Requests"
  | "People"
  | "Shift"
  | "Goals"
  | "Support";

type ActionItem = {
  /** i18n key (`home` namespace). */
  labelKey: string;
  icon: IoniconName;
  accent: AccentKey;
  route: ActionRoute;
};

/** Spec §4 order: two pages, 3 columns x 2 rows each. */
const ACTION_PAGES: ActionItem[][] = [
  [
    { labelKey: "actions.attendance", icon: "time-outline", accent: "blue", route: "Attendance" },
    { labelKey: "actions.leave", icon: "sunny-outline", accent: "green", route: "Leave" },
    { labelKey: "actions.payroll", icon: "wallet-outline", accent: "coral", route: "Payroll" },
    {
      labelKey: "actions.approvals",
      icon: "checkmark-done-outline",
      accent: "purple",
      route: "Approvals",
    },
    {
      labelKey: "actions.requests",
      icon: "document-text-outline",
      accent: "amber",
      route: "Requests",
    },
    { labelKey: "actions.people", icon: "people-outline", accent: "teal", route: "People" },
  ],
  [
    { labelKey: "actions.shift", icon: "calendar-outline", accent: "indigo", route: "Shift" },
    { labelKey: "actions.goals", icon: "flag-outline", accent: "pink", route: "Goals" },
    { labelKey: "actions.support", icon: "headset-outline", accent: "gray", route: "Support" },
  ],
];

const COLUMNS = 3;
const GAP = 12;
const SIDE_PADDING = 20;

type Props = {
  /** Pending approval count - shown as the Onaylarım badge when > 0. */
  approvalsCount: number;
  /** Attendance also owns the shift view and employee self-service check-in surfaces. */
  attendanceEnabled?: boolean;
  onNavigate: (route: ActionRoute) => void;
};

function chunkRows(items: ActionItem[]): ActionItem[][] {
  const rows: ActionItem[][] = [];
  for (let i = 0; i < items.length; i += COLUMNS) rows.push(items.slice(i, i + COLUMNS));
  return rows;
}

/** "İşlemlerim": horizontally paged tile grid with page dots (mockup `.pager` + `.dots`). */
export function ActionGrid({ approvalsCount, attendanceEnabled = true, onNavigate }: Props) {
  const { t } = useTranslation("home");
  const { width } = useWindowDimensions();
  const [page, setPage] = useState(0);
  const pages = attendanceEnabled
    ? ACTION_PAGES
    : ACTION_PAGES.map((items) =>
        items.filter((item) => item.route !== "Attendance" && item.route !== "Shift")
      );

  function onScrollEnd(event: NativeSyntheticEvent<NativeScrollEvent>) {
    setPage(Math.round(event.nativeEvent.contentOffset.x / width));
  }

  return (
    <View>
      <FlatList
        data={pages}
        horizontal
        pagingEnabled
        showsHorizontalScrollIndicator={false}
        keyExtractor={(_, index) => String(index)}
        onMomentumScrollEnd={onScrollEnd}
        renderItem={({ item: tiles }) => (
          <View style={[styles.page, { width }]}>
            {chunkRows(tiles).map((row) => (
              <View key={row[0].route} style={styles.row}>
                {row.map((tile) => (
                  <ActionTile
                    key={tile.route}
                    label={t(tile.labelKey)}
                    icon={tile.icon}
                    accent={tile.accent}
                    badge={tile.route === "Approvals" ? approvalsCount : undefined}
                    onPress={() => onNavigate(tile.route)}
                  />
                ))}
              </View>
            ))}
          </View>
        )}
      />
      <View
        style={styles.dots}
        accessibilityElementsHidden
        importantForAccessibility="no-hide-descendants"
      >
        {pages.map((_, index) => (
          <View key={index} style={[styles.dot, index === page && styles.dotActive]} />
        ))}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  page: {
    paddingHorizontal: SIDE_PADDING,
    paddingTop: 4,
    paddingBottom: 8,
    gap: GAP,
  },
  row: {
    flexDirection: "row",
    gap: GAP,
  },
  dots: {
    flexDirection: "row",
    justifyContent: "center",
    gap: 6,
    paddingTop: 6,
    paddingBottom: 2,
  },
  dot: {
    width: 6,
    height: 6,
    borderRadius: 3,
    backgroundColor: palette.glassBorder,
  },
  dotActive: {
    width: 22,
    backgroundColor: palette.brand,
  },
});
