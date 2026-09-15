/**
 * Access settings (Faz 0 item 1) - Ayarlar > Sistem ayarları > Erişim ayarları.
 * `GET/PUT /v1/identity/settings/access` (AccessSettingsController); PUT requires `identity.tenant.manage`.
 */
import { apiClient } from "@senseik/api-client";
import type { AccessSettingKey, AccessSettings } from "@/types";

export type { AccessSettingKey, AccessSettings } from "@/types";

const ACCESS_SETTINGS_PATH = "/v1/identity/settings/access";

export type AccessSettingGroup = "home" | "account" | "calendar";

/** Page layout: the three Kolay İK groups in on-screen order (labels live in the `settings` namespace). */
export const ACCESS_SETTING_GROUPS: { group: AccessSettingGroup; keys: AccessSettingKey[] }[] = [
  { group: "home", keys: ["showOrgInfo", "showUpcomingLeaves", "showUpcomingBirthdays", "showEventCalendar"] },
  {
    group: "account",
    keys: ["showFiles", "showAssets", "showWorkPhoneNumbers", "showOrgChart", "showCustomFields"],
  },
  { group: "calendar", keys: ["showCalendarMenu", "showAllLeavesInCalendar"] },
];

export async function getAccessSettings(): Promise<AccessSettings> {
  const { data } = await apiClient.get<AccessSettings>(ACCESS_SETTINGS_PATH);
  return data;
}

export async function updateAccessSettings(settings: AccessSettings): Promise<void> {
  await apiClient.put(ACCESS_SETTINGS_PATH, settings);
}
