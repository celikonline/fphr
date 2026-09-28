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
  label: string;
  icon: IoniconName;
  accent: AccentKey;
  route: ActionRoute;
};

/** Spec §4 order: two pages, 3 columns x 2 rows each. */
const ACTION_PAGES: ActionItem[][] = [
  [
    { label: "Puantaj", icon: "time-outline", accent: "blue", route: "Attendance" },
    { label: "İzinlerim", icon: "sunny-outline", accent: "green", route: "Leave" },
    { label: "Bordro", icon: "wallet-outline", accent: "coral", route: "Payroll" },
    { label: "Onaylarım", icon: "checkmark-done-outline", accent: "purple", route: "Approvals" },
    { label: "Taleplerim", icon: "document-text-outline", accent: "amber", route: "Requests" },
    { label: "Kişiler", icon: "people-outline", accent: "teal", route: "People" },
  ],
  [
    { label: "Vardiyam", icon: "calendar-outline", accent: "indigo", route: "Shift" },
    { label: "Hedeflerim", icon: "flag-outline", accent: "pink", route: "Goals" },
    { label: "Destek", icon: "headset-outline", accent: "gray", route: "Support" },
  ],
];

const COLUMNS = 3;
const GAP = 12;
const SIDE_PADDING = 20;

type Props = {
  /** Pending approval count - shown as the Onaylarım badge when > 0. */
  approvalsCount: number;
  onNavigate: (route: ActionRoute) => void;
};

function chunkRows(items: ActionItem[]): ActionItem[][] {
  const rows: ActionItem[][] = [];
  for (let i = 0; i < items.length; i += COLUMNS) rows.push(items.slice(i, i + COLUMNS));
  return rows;
}

/** "İşlemlerim": horizontally paged tile grid with page dots (mockup `.pager` + `.dots`). */
export function ActionGrid({ approvalsCount, onNavigate }: Props) {
  const { width } = useWindowDimensions();
  const [page, setPage] = useState(0);

  function onScrollEnd(event: NativeSyntheticEvent<NativeScrollEvent>) {
    setPage(Math.round(event.nativeEvent.contentOffset.x / width));
  }

  return (
    <View>
      <FlatList
        data={ACTION_PAGES}
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
                    label={tile.label}
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
        {ACTION_PAGES.map((_, index) => (
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
