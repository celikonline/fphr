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
/** Item 2 (Faz 0): Employee/Owner/Admin quick-select, mapped 1:1 to the matching system role codes. */
export type AccessType = "Employee" | "Owner" | "Admin";
const ACCESS_TYPE_CODES: ReadonlySet<string> = new Set<AccessType>(["Employee", "Owner", "Admin"]);

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
 * Derives the Employee/Owner/Admin "access type" from a user's role codes (thin client-side projection -
 * the backend keeps the three roles mutually exclusive via `PUT .../access-type`). Returns null when none
 * of the three access-type roles is assigned (e.g. a role-less invited user).
 */
export function deriveAccessType(roles: RoleRef[]): AccessType | null {
  const match = roles.find((r) => ACCESS_TYPE_CODES.has(r.code));
  return (match?.code as AccessType | undefined) ?? null;
}

export async function setUserAccessType(userId: string, accessType: AccessType): Promise<void> {
  await apiClient.put(`${USERS_BASE}/${userId}/access-type`, { accessType });
}

export async function setUserStatus(userId: string, enabled: boolean): Promise<void> {
  await apiClient.put(`${USERS_BASE}/${userId}/status`, { enabled });
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
