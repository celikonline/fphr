import type { RoleRef } from "@/services/users.service";

/**
 * Localized name of a role reference. System roles resolve through `users:roleNames.<code>` (the user list carries the
 * raw stored name/code, not the localized one the role catalog gets); custom roles keep their own name.
 */
export function roleDisplayName(t: (key: string, options?: Record<string, unknown>) => string, role: RoleRef): string {
  return t(`users:roleNames.${role.code}`, { defaultValue: role.name ?? role.code });
}
