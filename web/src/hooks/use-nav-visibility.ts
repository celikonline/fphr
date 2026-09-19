import { useMemo } from "react";
import { useAuthStore } from "@/store/auth.store";
import { moduleForPath } from "@/config/modules";
import type { AccessSettingKey } from "@/types";

/** senseik-052: dışa açık - route-guard.tsx aynı Gated şeklini rota bazlı yetki kontrolü için kullanıyor
 * (menü filtrelemesiyle aynı mekanizma, ayrı bir yetki kontrolü icat edilmiyor). */
export interface Gated {
  permissions?: readonly string[];
  accessSetting?: AccessSettingKey;
  /** Hidden when the toggleable module owning this path is turned off for the tenant (senseik-057). */
  path?: string;
}

/**
 * Filters menu/launcher/settings entries by any-of permissions, effective access settings and the tenant's
 * disabled modules (Ayarlar → Modüller).
 */
export function useVisibleItems<T extends Gated>(items: readonly T[]): T[] {
  const user = useAuthStore((state) => state.user);
  const features = useAuthStore((state) => state.features);

  return useMemo(() => {
    const permissions = new Set(user?.permissions ?? []);
    const disabledModules = new Set(features?.disabledModules ?? []);
    return items.filter((item) => {
      if (item.permissions && item.permissions.length > 0 && !item.permissions.some((p) => permissions.has(p))) {
        return false;
      }
      if (item.path) {
        const owner = moduleForPath(item.path);
        if (owner && disabledModules.has(owner)) {
          return false;
        }
      }
      if (item.accessSetting) {
        return !!features && (features.accessSettingsBypass || features.access[item.accessSetting] === true);
      }
      return true;
    });
  }, [items, user, features]);
}
