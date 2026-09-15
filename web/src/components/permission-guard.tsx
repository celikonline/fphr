import { ReactNode } from "react";
import { useAuthStore } from "@/store/auth.store";

interface PermissionGuardProps {
  /** Permission key, e.g. "organization.units.delete" (module.resource_plural.action). */
  permission?: string;
  /** Any-of list of permission keys - passes if the user has at least one. */
  anyOf?: string[];
  children: ReactNode;
  /** Rendered instead of children when the permission check fails (default: nothing). */
  fallback?: ReactNode;
}

/**
 * Hides (or swaps) UI based on the current user's permissions.
 * Backed by authStore.hasPermission - the single source of truth for permission checks.
 *
 * Usage:
 *   <PermissionGuard permission="organization.units.delete">
 *     <Button variant="destructive">Delete</Button>
 *   </PermissionGuard>
 */
export function PermissionGuard({ permission, anyOf, children, fallback = null }: PermissionGuardProps) {
  const hasPermission = useAuthStore((state) => state.hasPermission);

  const allowed = permission
    ? hasPermission(permission)
    : anyOf
      ? anyOf.some((p) => hasPermission(p))
      : true;

  return allowed ? <>{children}</> : <>{fallback}</>;
}

/** Hook form of the same check, for disabling buttons / gating logic inline. */
export function usePermission(permission: string): boolean {
  return useAuthStore((state) => state.hasPermission(permission));
}

export function useAnyPermission(permissions: string[]): boolean {
  return useAuthStore((state) => permissions.some((p) => state.hasPermission(p)));
}
