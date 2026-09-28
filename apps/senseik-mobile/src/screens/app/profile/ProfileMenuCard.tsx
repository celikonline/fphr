import React, { Fragment } from "react";
import { StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { Ionicons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { GlassCard } from "@/components/ui";
import { palette, text } from "@/theme/theme";

type IoniconName = React.ComponentProps<typeof Ionicons>["name"];

/** Parameterless `AppStackParamList` screens the profile menu links to. */
export type ProfileMenuRoute =
  "People" | "Goals" | "Requests" | "Leave" | "NotificationPreferences" | "Support";

/** `labelKey` is an i18n key - render with `t(item.labelKey)`. */
type MenuItem = { route: ProfileMenuRoute; labelKey: string; icon: IoniconName };

/** Spec/brief order: Kişiler, Hedeflerim, Taleplerim, İzinlerim, Bildirim tercihleri, Destek. */
export const PROFILE_MENU_ITEMS: MenuItem[] = [
  { route: "People", labelKey: "home:actions.people", icon: "people-outline" },
  { route: "Goals", labelKey: "home:actions.goals", icon: "flag-outline" },
  { route: "Requests", labelKey: "home:actions.requests", icon: "document-text-outline" },
  { route: "Leave", labelKey: "home:actions.leave", icon: "sunny-outline" },
  {
    route: "NotificationPreferences",
    labelKey: "home:menu.notificationPreferences",
    icon: "notifications-outline",
  },
  { route: "Support", labelKey: "home:actions.support", icon: "headset-outline" },
];

type Props = {
  onNavigate: (route: ProfileMenuRoute) => void;
};

/** Icon + label + chevron rows, one glass card (brief: "menü kartı ... ikonlu satırlar, ›"). */
export function ProfileMenuCard({ onNavigate }: Props) {
  const { t } = useTranslation();
  return (
    <GlassCard style={styles.card} padding={0}>
      {PROFILE_MENU_ITEMS.map((item, index) => (
        <Fragment key={item.route}>
          {index > 0 ? <View style={styles.separator} /> : null}
          <TouchableOpacity
            style={styles.row}
            onPress={() => onNavigate(item.route)}
            accessibilityRole="button"
            accessibilityLabel={t(item.labelKey)}
          >
            <Ionicons name={item.icon} size={20} color={palette.ice} />
            <Text style={styles.label}>{t(item.labelKey)}</Text>
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
