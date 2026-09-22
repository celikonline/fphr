/**
 * Users & Roles Service
 *
 * Screen 5 (Item 20): user list, role assignment matrix, permission preview.
 *
 * Unlike the other Item 20 services, this one is wired to REAL backend endpoints -
 * `Sense.Modules.Identity` already implements users/roles/permissions
 * (UsersController, RolesController, PermissionsController) with the project's grid standard
 * (?page&pageSize&sort&q). Base paths mirror `IdentityRoutes` in AuthController.cs.
 */
import { apiClient } from "@senseik/api-client";
import type { PagedResult, PagedQueryParams } from "@/types";

export type UserStatus = "invited" | "active" | "locked" | "disabled";
export type DataScope = "self" | "team" | "department" | "location" | "company" | "custom";
/**
 * "Kullanıcı grubu" quick-select (İK Yöneticisi / Muhasebe / Çalışan), mapped 1:1 to the `HR`, `PayrollSpecialist`
 * and `Employee` system role codes. İK Yöneticisi and Muhasebe can do everything in the company; Çalışan is self-service.
 * Order = display order and `deriveAccessType` precedence.
 */
export type AccessType = "HR" | "PayrollSpecialist" | "Employee";
export const ACCESS_TYPES: readonly AccessType[] = ["HR", "PayrollSpecialist", "Employee"];

/**
 * System role codes never shown in company screens (role lists, role chips, role pickers): `Owner` is carried in the
 * background by the company's first İK Yöneticisi (last-owner protection, always paired with `HR`), `SuperAdmin` is
 * platform-only.
 */
export const HIDDEN_ROLE_CODES: ReadonlySet<string> = new Set(["Owner", "SuperAdmin"]);

export function isHiddenRole(code: string): boolean {
  return HIDDEN_ROLE_CODES.has(code);
}

/** The roles a company screen may show - everything except {@link HIDDEN_ROLE_CODES}. */
export function visibleRoles<T extends { code: string }>(roles: readonly T[]): T[] {
  return roles.filter((role) => !isHiddenRole(role.code));
}

/**
 * Role ids to save from a picker that never showed the hidden roles: the picked ids plus every hidden role the user
 * already has, so saving the picker can't silently strip `Owner` (the backend rejects that as a last-owner /
 * owner-only change).
 */
export function withKeptHiddenRoleIds(currentRoles: readonly RoleRef[], pickedRoleIds: readonly string[]): string[] {
  const kept = currentRoles.filter((role) => isHiddenRole(role.code)).map((role) => role.id);
  return Array.from(new Set([...pickedRoleIds, ...kept]));
}

export interface RoleRef {
  id: string;
  code: string;
  name: string | null;
}

export interface UserListItem {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  phone: string | null;
  status: UserStatus;
  employeeId: string | null;
  mfaEnabled: boolean;
  lastLoginAt: string | null;
  roles: RoleRef[];
  createdAt: string;
}

export interface RolePermission {
  permission: string;
  scope: DataScope;
  scopeIds: string[] | null;
}

export interface Role {
  id: string;
  code: string;
  name: string | null;
  description: string | null;
  isSystem: boolean;
  isLocked: boolean;
  userCount: number;
  permissions: RolePermission[];
}

export interface Permission {
  key: string;
  module: string;
  displayName: string;
  group: string;
  description: string | null;
}

export interface PermissionGroup {
  module: string;
  group: string;
  permissions: Permission[];
}

const USERS_BASE = "/v1/identity/users";
const ROLES_BASE = "/v1/identity/roles";
const PERMISSIONS_BASE = "/v1/identity/permissions";

export async function listUsers(query: PagedQueryParams = {}): Promise<PagedResult<UserListItem>> {
  const { data } = await apiClient.get<PagedResult<UserListItem>>(USERS_BASE, { params: query });
  return data;
}

export async function setUserRoles(userId: string, roleIds: string[]): Promise<void> {
  await apiClient.put(`${USERS_BASE}/${userId}/roles`, { roleIds });
}

/**
 * Derives the user group (HR / PayrollSpecialist / Employee) from a user's role codes (thin client-side projection -
 * the backend keeps the three roles mutually exclusive via `PUT .../access-type`). Precedence HR > PayrollSpecialist >
 * Employee, so an owner (who always also has HR) shows as İK Yöneticisi. Returns null when none of the three roles is
 * assigned (e.g. a role-less invited user).
 */
export function deriveAccessType(roles: readonly RoleRef[]): AccessType | null {
  const codes = new Set(roles.map((r) => r.code));
  return ACCESS_TYPES.find((type) => codes.has(type)) ?? null;
}

export async function setUserAccessType(userId: string, accessType: AccessType): Promise<void> {
  await apiClient.put(`${USERS_BASE}/${userId}/access-type`, { accessType });
}

export async function setUserStatus(userId: string, enabled: boolean): Promise<void> {
  await apiClient.put(`${USERS_BASE}/${userId}/status`, { enabled });
}

/**
 * Admin MFA reset (S-12, gün açılışı tur 1 sözleşmesi): clears the user's TOTP enrollment so they can set up
 * MFA again from scratch. `identity.users.manage`-gated on this screen (same as the rest of the action menu).
 * Errors: 409 `identity.mfa.self_reset_forbidden` (can't reset your own MFA this way — use the security card
 * instead), 404 for a different tenant's user.
 */
export async function resetUserMfa(userId: string): Promise<void> {
  await apiClient.post(`${USERS_BASE}/${userId}/mfa/reset`);
}

/**
 * Bulk-assigns one role to many users at once (Item 20 requirement). There is no dedicated bulk
 * endpoint on the backend, so this merges the role into each user's existing role set and issues
 * one `PUT .../roles` call per user - acceptable for the ~50-user demo dataset this screen targets.
 */
export async function bulkAssignRole(users: UserListItem[], roleId: string): Promise<void> {
  await Promise.all(
    users.map((user) => {
      const roleIds = new Set(user.roles.map((r) => r.id));
      roleIds.add(roleId);
      return setUserRoles(user.id, Array.from(roleIds));
    })
  );
}

export async function listRoles(): Promise<Role[]> {
  const { data } = await apiClient.get<Role[]>(ROLES_BASE);
  return data;
}

export async function getRole(id: string): Promise<Role> {
  const { data } = await apiClient.get<Role>(`${ROLES_BASE}/${id}`);
  return data;
}

export async function listPermissionGroups(): Promise<PermissionGroup[]> {
  const { data } = await apiClient.get<PermissionGroup[]>(PERMISSIONS_BASE);
  return data;
}
