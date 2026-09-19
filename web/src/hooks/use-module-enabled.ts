import { useLocation } from "react-router";
import { useAuthStore } from "@/store/auth.store";
import { moduleForPath, type ToggleableModuleKey } from "@/config/modules";

/**
 * senseik-057: false when the tenant turned this product module off (Ayarlar → Modüller, `/identity/me`
 * `features.disabledModules`). Use it to skip queries/widgets of a disabled module - the API would answer 403
 * `module.disabled` anyway.
 */
export function useModuleEnabled(key: ToggleableModuleKey): boolean {
  return useAuthStore((state) => !(state.features?.disabledModules ?? []).includes(key));
}

/** The disabled module owning the current route, or null when the route is core or its module is on. */
export function useDisabledModuleForCurrentRoute(): ToggleableModuleKey | null {
  const { pathname } = useLocation();
  const disabled = useAuthStore((state) => state.features?.disabledModules);
  const owner = moduleForPath(pathname);
  return owner && (disabled ?? []).includes(owner) ? owner : null;
}
