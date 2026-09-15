import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { listOrgLevelOptions, listOrgUnitOptions, type EmployeeOrgUnitOption } from "@/services/employees.service";

export interface OrgUnitTreeOption extends EmployeeOrgUnitOption {
  depth: number;
}

/**
 * Active org units for employee screens: name lookup + depth-first ordered options.
 * Callers without `organization.units.read` get a 403 here; names then fall back to "—".
 */
export function useOrgUnits() {
  const query = useQuery({
    queryKey: ["employees", "org-unit-options"],
    queryFn: listOrgUnitOptions,
    retry: false,
    staleTime: 5 * 60_000,
  });

  return useMemo(() => {
    const units = query.data ?? [];
    const nameById = new Map(units.map((u) => [u.id, u.name]));
    const byParent = new Map<string | null, EmployeeOrgUnitOption[]>();
    const ids = new Set(units.map((u) => u.id));
    for (const unit of units) {
      const parent = unit.parentId && ids.has(unit.parentId) ? unit.parentId : null;
      const bucket = byParent.get(parent) ?? [];
      bucket.push(unit);
      byParent.set(parent, bucket);
    }
    const ordered: OrgUnitTreeOption[] = [];
    const walk = (parent: string | null, depth: number) => {
      const children = (byParent.get(parent) ?? []).sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name));
      for (const child of children) {
        ordered.push({ ...child, depth });
        walk(child.id, depth + 1);
      }
    };
    walk(null, 0);
    return { options: ordered, nameById, isLoading: query.isLoading };
  }, [query.data, query.isLoading]);
}

/** Active hierarchy levels in SortOrder (Şirket first) for the per-level career selects; empty without `organization` read access. */
export function useOrgLevels() {
  const query = useQuery({
    queryKey: ["employees", "org-level-options"],
    queryFn: listOrgLevelOptions,
    retry: false,
    staleTime: 5 * 60_000,
  });

  return useMemo(
    () => ({ levels: [...(query.data ?? [])].sort((a, b) => a.sortOrder - b.sortOrder), isLoading: query.isLoading }),
    [query.data, query.isLoading],
  );
}
