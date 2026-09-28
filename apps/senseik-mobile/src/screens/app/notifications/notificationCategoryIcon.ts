import { Ionicons } from "@expo/vector-icons";
import type { ComponentProps } from "react";
import type { AccentKey } from "@/theme/theme";

type IoniconName = ComponentProps<typeof Ionicons>["name"];

/** N1: each `templateKey`'s dot-separated first segment ("payroll.payslip_ready" -> "payroll")
 * picks the category icon/accent - `UserNotification` carries no explicit `category` field. */
const CATEGORY_ICONS: Record<string, { icon: IoniconName; accent: AccentKey }> = {
  request: { icon: "document-text-outline", accent: "amber" },
  approval: { icon: "checkmark-done-outline", accent: "purple" },
  documents: { icon: "document-attach-outline", accent: "teal" },
  ohs: { icon: "alert-circle-outline", accent: "coral" },
  employee: { icon: "people-outline", accent: "blue" },
  announcement: { icon: "megaphone-outline", accent: "indigo" },
  payroll: { icon: "wallet-outline", accent: "coral" },
  leave: { icon: "sunny-outline", accent: "green" },
  onboarding: { icon: "flag-outline", accent: "pink" },
  account: { icon: "person-circle-outline", accent: "gray" },
};

const DEFAULT_CATEGORY_ICON: { icon: IoniconName; accent: AccentKey } = {
  icon: "notifications-outline",
  accent: "gray",
};

/** Maps a notification's `templateKey` to the category icon/accent shown on its row. */
export function notificationCategoryIcon(templateKey: string): {
  icon: IoniconName;
  accent: AccentKey;
} {
  const prefix = templateKey.split(".")[0];
  return CATEGORY_ICONS[prefix] ?? DEFAULT_CATEGORY_ICON;
}
