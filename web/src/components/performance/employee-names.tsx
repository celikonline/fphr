import { useMemo } from "react";
import { useQueries } from "@tanstack/react-query";
import { getEmployee } from "@/services/employees.service";

/**
 * Resolves employee display names for a set of ids one-by-one (no batch-by-ids endpoint exists yet) - React Query
 * dedupes/caches per id, so re-rendering with an overlapping id set is cheap. Falls back to the raw id while loading.
 */
export function useEmployeeNames(ids: string[]): (id: string) => string {
  const unique = useMemo(() => Array.from(new Set(ids.filter(Boolean))), [ids]);

  const results = useQueries({
    queries: unique.map((id) => ({
      queryKey: ["employees", "detail", id],
      queryFn: () => getEmployee(id),
      staleTime: 5 * 60 * 1000,
    })),
  });

  const nameById = useMemo(() => {
    const map = new Map<string, string>();
    unique.forEach((id, index) => {
      const name = results[index]?.data?.displayName;
      if (name) map.set(id, name);
    });
    return map;
  }, [unique, results]);

  return (id: string) => nameById.get(id) ?? id;
}
