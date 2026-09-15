import { useMemo } from "react";
import { useAuthStore } from "@/store/auth.store";
import type { AccessSettingKey } from "@/types";

interface Gated {
  permissions?: readonly string[];
  accessSetting?: AccessSettingKey;
}

/** Filters menu/launcher/settings entries by any-of permissions and effective access settings. */
export function useVisibleItems<T extends Gated>(items: readonly T[]): T[] {
  const user = useAuthStore((state) => state.user);
  const features = useAuthStore((state) => state.features);

  return useMemo(() => {
    const permissions = new Set(user?.permissions ?? []);
    return items.filter((item) => {
      if (item.permissions && item.permissions.length > 0 && !item.permissions.some((p) => permissions.has(p))) {
        return false;
      }
      if (item.accessSetting) {
        return !!features && (features.accessSettingsBypass || features.access[item.accessSetting] === true);
      }
      return true;
    });
  }, [items, user, features]);
}
