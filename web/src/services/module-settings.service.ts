/**
 * Module settings (senseik-057) - Ayarlar > Sistem ayarları > Modüller.
 * `GET/PUT /v1/identity/settings/modules` (ModuleSettingsController); PUT requires `identity.modules.manage`.
 * Every user gets the lightweight form in `/identity/me` → `features.disabledModules`.
 */
import { apiClient } from "@senseik/api-client";
import type { ModuleSettings } from "@/types";

export type { ModuleSettings, ModuleSettingItem } from "@/types";

const MODULE_SETTINGS_PATH = "/v1/identity/settings/modules";

export async function getModuleSettings(): Promise<ModuleSettings> {
  const { data } = await apiClient.get<ModuleSettings>(MODULE_SETTINGS_PATH);
  // nulls are omitted by the API; normalize so callers can rely on arrays.
  return { modules: data.modules ?? [], disabledModules: data.disabledModules ?? [] };
}

export async function updateModuleSettings(disabledModules: string[]): Promise<void> {
  await apiClient.put(MODULE_SETTINGS_PATH, { disabledModules });
}
