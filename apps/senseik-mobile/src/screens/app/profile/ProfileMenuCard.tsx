import React, { Fragment } from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { GlassCard } from "@/components/ui";
import { palette, text } from "@/theme/theme";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];

/** Parameterless `AppStackParamList` screens the profile menu links to. */
export type ProfileMenuRoute =
  "People" | "Goals" | "Requests" | "Leave" | "NotificationPreferences" | "Support";

type MenuItem = { route: ProfileMenuRoute; label: string; icon: IoniconName };

/** Spec/brief order: Kişiler, Hedeflerim, Taleplerim, İzinlerim, Bildirim tercihleri, Destek. */
export const PROFILE_MENU_ITEMS: MenuItem[] = [
  { route: "People", label: "Kişiler", icon: "people-outline" },
  { route: "Goals", label: "Hedeflerim", icon: "flag-outline" },
  { route: "Requests", label: "Taleplerim", icon: "document-text-outline" },
  { route: "Leave", label: "İzinlerim", icon: "sunny-outline" },
  { route: "NotificationPreferences", label: "Bildirim tercihleri", icon: "notifications-outline" },
  { route: "Support", label: "Destek", icon: "headset-outline" },
];

type Props = {
  onNavigate: (route: ProfileMenuRoute) => void;
};

/** Icon + label + chevron rows, one glass card (brief: "menü kartı ... ikonlu satırlar, ›"). */
export function ProfileMenuCard({ onNavigate }: Props) {
  return (
    <GlassCard style={styles.card} padding={0}>
      {PROFILE_MENU_ITEMS.map((item, index) => (
        <Fragment key={item.route}>
          {index > 0 ? <View style={styles.separator} /> : null}
          <TouchableOpacity
            style={styles.row}
            onPress={() => onNavigate(item.route)}
            accessibilityRole="button"
            accessibilityLabel={item.label}
          >
            <Ionicons name={item.icon} size={20} color={palette.ice} />
            <Text style={styles.label}>{item.label}</Text>
            <Ionicons name="chevron-forward" size={18} color={palette.textFaint} />
          </TouchableOpacity>
        </Fragment>
      ))}
    </GlassCard>
  );
}

const styles = StyleSheet.create({
  card: {
    marginHorizontal: 20,
    marginBottom: 16,
  },
  row: {
    flexDirection: "row",
    alignItems: "center",
    gap: 12,
    minHeight: 52,
    paddingHorizontal: 16,
  },
  label: {
    ...text.body,
    flex: 1,
  },
  separator: {
    height: 1,
    marginHorizontal: 16,
    backgroundColor: palette.line,
  },
});
