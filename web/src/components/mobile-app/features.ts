import { Bell, CalendarDays, CheckCheck, FilePlus, Sun, Timer } from "lucide-react";
import type { LucideIcon } from "lucide-react";

export interface MobileAppFeature {
  /** i18n key under "mobileApp:features.<key>" and the screenshot's basename in /images/mobile-app/. */
  key: string;
  icon: LucideIcon;
  /** Filename under web/public/images/mobile-app/. */
  image: string;
}

/** "Uygulamada neler var" section: order matches the kabul list, first entry is the default selection. */
export const MOBILE_APP_FEATURES: MobileAppFeature[] = [
  { key: "clockIn", icon: Timer, image: "clock-in.jpg" },
  { key: "leave", icon: Sun, image: "leave.jpg" },
  { key: "requests", icon: FilePlus, image: "new-request.jpg" },
  { key: "approvals", icon: CheckCheck, image: "approvals.jpg" },
  { key: "shifts", icon: CalendarDays, image: "shifts.jpg" },
  { key: "notifications", icon: Bell, image: "notifications.jpg" },
];
