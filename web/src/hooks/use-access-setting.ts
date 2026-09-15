import { useAuthStore } from "@/store/auth.store";
import { getImpersonatorId } from "@/lib/jwt";
import type { AccessSettingKey } from "@/types";

/**
 * Effective access setting for the current user (from /identity/me `features`): true when the tenant switch is on
 * or the caller bypasses access settings (Owner/Admin/HR). UI filtering only - the server enforces the same rule.
 */
export function useAccessSetting(key: AccessSettingKey): boolean {
  return useAuthStore((state) => state.isAccessSettingEnabled(key));
}

/** Platform user id from the JWT `act` claim when the session runs under a support-access grant, else null. */
export function useImpersonatorId(): string | null {
  return useAuthStore((state) => getImpersonatorId(state.token));
}
